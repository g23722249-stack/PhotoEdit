Imports Microsoft.ML.OnnxRuntime
Imports Microsoft.ML.OnnxRuntime.Tensors

''' <summary>執行提供者偏好。</summary>
Public Enum ExecutionPreference
    ''' <summary>先試 DirectML，失敗改用 CPU。</summary>
    Auto
    DirectML
    Cpu
End Enum

''' <summary>一個輸入或輸出的規格。</summary>
Public NotInheritable Class TensorSpec
    Public Property Name As String = ""
    ''' <summary>維度；動態維度為 -1。</summary>
    Public Property Shape As Long() = Array.Empty(Of Long)()
    Public Property ElementType As String = ""

    Public Overrides Function ToString() As String
        Return $"{Name} [{String.Join(",", Shape.Select(Function(d) If(d < 0, "?", d.ToString())))}] {ElementType}"
    End Function
End Class

''' <summary>載入後的模型與驗證報告。</summary>
Public NotInheritable Class LoadedModel
    Implements IDisposable

    Public Property Session As InferenceSession
    Public Property ModelPath As String = ""
    ''' <summary>實際使用的執行提供者："DirectML" 或 "CPU"。</summary>
    Public Property Provider As String = "CPU"
    Public Property Inputs As New List(Of TensorSpec)()
    Public Property Outputs As New List(Of TensorSpec)()
    Public Property Header As OnnxHeader
    Public Property Warnings As New List(Of String)()
    Public Property LoadMilliseconds As Double

    Public Function Describe() As String
        Dim sb As New Text.StringBuilder()
        sb.Append($"執行提供者 {Provider}；opset {If(Header Is Nothing, 0, Header.DefaultOpset)}；IR {If(Header Is Nothing, 0, Header.IrVersion)}；")
        sb.Append($"輸入 {String.Join("、", Inputs)}；輸出 {String.Join("、", Outputs)}；載入 {LoadMilliseconds:0} ms")
        If Warnings.Count > 0 Then sb.Append("；警告：" & String.Join("；", Warnings))
        Return sb.ToString()
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Session?.Dispose()
        Session = Nothing
    End Sub
End Class

Public Class ModelValidationException
    Inherits Exception
    Public Sub New(message As String)
        MyBase.New(message)
    End Sub
End Class

''' <summary>
''' 建立 ONNX Runtime 工作階段：依偏好嘗試 DirectML（必要的設定：關閉記憶體樣式、循序執行），
''' 不支援時（沒有 DirectML 版原生程式庫、顯卡不支援、算子不相容）自動改用 CPU 並記錄原因。
''' 載入後讀取輸入輸出 metadata 與 opset，和模型清單規格比對；形狀或型別不相容時拒絕使用。
''' 以檔案路徑建立工作階段，外部權重檔（.onnx_data）才找得到。
''' </summary>
Public NotInheritable Class OnnxModelLoader
    Public Function Load(modelPath As String, preference As ExecutionPreference, Optional expected As ManifestIo = Nothing) As LoadedModel
        If Not IO.File.Exists(modelPath) Then Throw New IO.FileNotFoundException("找不到模型檔，請先在「模型管理」下載。", modelPath)
        Dim sw = Stopwatch.StartNew()
        Dim result As New LoadedModel With {.ModelPath = modelPath}
        Try
            result.Header = OnnxHeaderReader.Read(modelPath)
        Catch ex As Exception When TypeOf ex Is IO.InvalidDataException OrElse TypeOf ex Is IO.EndOfStreamException
            Throw New ModelValidationException($"模型檔無效：{ex.Message}")
        End Try

        If preference <> ExecutionPreference.Cpu Then
            Try
                result.Session = CreateSession(modelPath, True)
                result.Provider = "DirectML"
            Catch ex As Exception When TypeOf ex Is OnnxRuntimeException OrElse TypeOf ex Is EntryPointNotFoundException OrElse
                                       TypeOf ex Is DllNotFoundException OrElse TypeOf ex Is NotSupportedException OrElse TypeOf ex Is TypeInitializationException
                If preference = ExecutionPreference.DirectML Then
                    result.Warnings.Add($"DirectML 無法使用（{FirstLine(ex.Message)}），已改用 CPU。")
                Else
                    result.Warnings.Add($"DirectML 不可用，使用 CPU（{FirstLine(ex.Message)}）。")
                End If
                AppLog.Warn($"DirectML 載入失敗，改用 CPU：{ex.Message}")
            End Try
        End If
        If result.Session Is Nothing Then
            result.Session = CreateSession(modelPath, False)
            result.Provider = "CPU"
        End If

        For Each kv In result.Session.InputMetadata
            result.Inputs.Add(ToSpec(kv.Key, kv.Value))
        Next
        For Each kv In result.Session.OutputMetadata
            result.Outputs.Add(ToSpec(kv.Key, kv.Value))
        Next
        If expected IsNot Nothing Then
            Try
                Validate(result, expected)
            Catch
                result.Dispose()
                Throw
            End Try
        End If
        result.LoadMilliseconds = sw.Elapsed.TotalMilliseconds
        AppLog.Info($"載入模型 {IO.Path.GetFileName(modelPath)}：{result.Describe()}")
        Return result
    End Function

    Private Shared Function CreateSession(path As String, directML As Boolean) As InferenceSession
        Dim opts As New SessionOptions With {.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL}
        Try
            If directML Then
                opts.EnableMemoryPattern = False
                opts.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
                opts.AppendExecutionProvider_DML(0)
            End If
            Return New InferenceSession(path, opts)
        Finally
            opts.Dispose()
        End Try
    End Function

    Private Shared Function ToSpec(name As String, m As NodeMetadata) As TensorSpec
        Dim shape As Long() = If(m.Dimensions Is Nothing, Array.Empty(Of Long)(), m.Dimensions.Select(Function(d) CLng(d)).ToArray())
        Return New TensorSpec With {.Name = name, .Shape = shape, .ElementType = TypeName(m.ElementDataType)}
    End Function

    Public Shared Function TypeName(t As TensorElementType) As String
        Select Case t
            Case TensorElementType.Float : Return "float32"
            Case TensorElementType.Float16 : Return "float16"
            Case TensorElementType.Double : Return "float64"
            Case TensorElementType.UInt8 : Return "uint8"
            Case TensorElementType.Int64 : Return "int64"
            Case TensorElementType.Int32 : Return "int32"
            Case Else : Return t.ToString().ToLowerInvariant()
        End Select
    End Function

    ''' <summary>和清單規格比對：名稱（有指定時）、秩、固定維度、型別。動態維度只記警告。</summary>
    Public Shared Sub Validate(model As LoadedModel, expected As ManifestIo)
        Dim input = If(String.IsNullOrEmpty(expected.InputName), model.Inputs.FirstOrDefault(), model.Inputs.FirstOrDefault(Function(i) i.Name = expected.InputName))
        If input Is Nothing Then Throw New ModelValidationException($"模型沒有名為 {expected.InputName} 的輸入；實際輸入：{String.Join("、", model.Inputs)}")
        CheckSpec(model, input, expected.InputShape, expected.InputType, "輸入")
        If Not String.IsNullOrEmpty(expected.OutputName) Then
            Dim output = model.Outputs.FirstOrDefault(Function(o) o.Name = expected.OutputName)
            If output Is Nothing Then Throw New ModelValidationException($"模型沒有名為 {expected.OutputName} 的輸出；實際輸出：{String.Join("、", model.Outputs)}")
            CheckSpec(model, output, expected.OutputShape, expected.OutputType, "輸出")
        ElseIf expected.OutputShape IsNot Nothing AndAlso expected.OutputShape.Length > 0 Then
            Dim ok = model.Outputs.Any(Function(o) ShapeCompatible(o.Shape, expected.OutputShape))
            If Not ok Then Throw New ModelValidationException($"找不到形狀相容於 [{String.Join(",", expected.OutputShape)}] 的輸出；實際輸出：{String.Join("、", model.Outputs)}")
        End If
    End Sub

    Private Shared Sub CheckSpec(model As LoadedModel, actual As TensorSpec, shape As Long(), type As String, what As String)
        If Not String.IsNullOrEmpty(type) AndAlso actual.ElementType <> type Then
            If (type = "float32" OrElse type = "float16") AndAlso (actual.ElementType = "float32" OrElse actual.ElementType = "float16") Then
                model.Warnings.Add($"{what} {actual.Name} 型別為 {actual.ElementType}（清單預期 {type}），已自動轉換。")
            Else
                Throw New ModelValidationException($"{what} {actual.Name} 型別為 {actual.ElementType}，清單預期 {type}，不相容。")
            End If
        End If
        If shape Is Nothing OrElse shape.Length = 0 Then Return
        If actual.Shape.Length <> shape.Length Then Throw New ModelValidationException($"{what} {actual.Name} 維度數為 {actual.Shape.Length}，清單預期 {shape.Length}（[{String.Join(",", shape)}]）。")
        For i = 0 To shape.Length - 1
            If actual.Shape(i) < 0 Then
                model.Warnings.Add($"{what} {actual.Name} 第 {i} 維為動態，使用清單值 {shape(i)}。")
            ElseIf shape(i) > 0 AndAlso actual.Shape(i) <> shape(i) Then
                Throw New ModelValidationException($"{what} {actual.Name} 第 {i} 維為 {actual.Shape(i)}，清單預期 {shape(i)}。")
            End If
        Next
    End Sub

    Public Shared Function ShapeCompatible(actual As Long(), expected As Long()) As Boolean
        If actual.Length <> expected.Length Then Return False
        For i = 0 To actual.Length - 1
            If actual(i) > 0 AndAlso expected(i) > 0 AndAlso actual(i) <> expected(i) Then Return False
        Next
        Return True
    End Function

    Private Shared Function FirstLine(s As String) As String
        Dim i = s.IndexOfAny({ChrW(13), ChrW(10)})
        Return If(i > 0, s.Substring(0, i), s)
    End Function
End Class
