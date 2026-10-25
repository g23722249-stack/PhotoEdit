Imports System.IO

''' <summary>ONNX 檔頭資訊（不載入權重）。</summary>
Public NotInheritable Class OnnxHeader
    Public Property IrVersion As Long
    Public Property ProducerName As String = ""
    Public Property ProducerVersion As String = ""
    ''' <summary>運算子集版本（domain → version；預設 domain 為空字串 "ai.onnx"）。</summary>
    Public Property Opsets As New Dictionary(Of String, Long)()

    Public ReadOnly Property DefaultOpset As Long
        Get
            Dim v As Long
            If Opsets.TryGetValue("", v) Then Return v
            If Opsets.TryGetValue("ai.onnx", v) Then Return v
            Return 0
        End Get
    End Property
End Class

''' <summary>
''' 讀 ONNX（protobuf ModelProto）最上層欄位：ir_version(1)、producer_name(2)、producer_version(3)、opset_import(8)。
''' graph(7) 可能有好幾百 MB，直接依長度跳過，不讀入記憶體。ONNX Runtime 的 C# API 不提供 opset，所以自己解析。
''' </summary>
Public NotInheritable Class OnnxHeaderReader
    Private Sub New()
    End Sub

    Public Shared Function Read(path As String) As OnnxHeader
        Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536)
            Return Read(fs)
        End Using
    End Function

    Public Shared Function Read(s As Stream) As OnnxHeader
        Dim h As New OnnxHeader()
        While s.Position < s.Length
            Dim key = ReadVarint(s)
            Dim field = CInt(key >> 3), wire = CInt(key And 7UL)
            Select Case wire
                Case 0
                    Dim v = ReadVarint(s)
                    If field = 1 Then h.IrVersion = CLng(v)
                Case 2
                    Dim len = CLng(ReadVarint(s))
                    If len < 0 OrElse s.Position + len > s.Length Then Throw New InvalidDataException("ONNX 檔案損壞或不完整（長度欄位超出檔尾）。")
                    Select Case field
                        Case 2 : h.ProducerName = ReadString(s, len)
                        Case 3 : h.ProducerVersion = ReadString(s, len)
                        Case 8
                            Dim buf(CInt(len) - 1) As Byte
                            ReadExact(s, buf)
                            Using ms As New MemoryStream(buf)
                                Dim domain = "", version = 0L
                                While ms.Position < ms.Length
                                    Dim k = ReadVarint(ms)
                                    Dim f = CInt(k >> 3), w = CInt(k And 7UL)
                                    If f = 1 AndAlso w = 2 Then
                                        domain = ReadString(ms, CLng(ReadVarint(ms)))
                                    ElseIf f = 2 AndAlso w = 0 Then
                                        version = CLng(ReadVarint(ms))
                                    Else
                                        Skip(ms, w)
                                    End If
                                End While
                                h.Opsets(domain) = version
                            End Using
                        Case Else
                            s.Seek(len, SeekOrigin.Current)
                    End Select
                Case 1 : s.Seek(8, SeekOrigin.Current)
                Case 5 : s.Seek(4, SeekOrigin.Current)
                Case Else
                    Throw New InvalidDataException($"不是有效的 ONNX 檔（protobuf wire type {wire}）。")
            End Select
        End While
        If h.IrVersion = 0 AndAlso h.Opsets.Count = 0 Then Throw New InvalidDataException("不是有效的 ONNX 檔（找不到 ir_version 與 opset_import）。")
        Return h
    End Function

    Private Shared Sub Skip(s As Stream, wire As Integer)
        Select Case wire
            Case 0 : ReadVarint(s)
            Case 1 : s.Seek(8, SeekOrigin.Current)
            Case 2 : s.Seek(CLng(ReadVarint(s)), SeekOrigin.Current)
            Case 5 : s.Seek(4, SeekOrigin.Current)
            Case Else : Throw New InvalidDataException("protobuf 格式錯誤。")
        End Select
    End Sub

    Private Shared Function ReadVarint(s As Stream) As ULong
        Dim result = 0UL
        Dim shift = 0
        Do
            Dim b = s.ReadByte()
            If b < 0 Then Throw New EndOfStreamException("ONNX 檔案不完整。")
            result = result Or (CULng(b And &H7F) << shift)
            If (b And &H80) = 0 Then Return result
            shift += 7
            If shift > 63 Then Throw New InvalidDataException("protobuf varint 過長。")
        Loop
    End Function

    Private Shared Function ReadString(s As Stream, len As Long) As String
        Dim buf(CInt(len) - 1) As Byte
        ReadExact(s, buf)
        Return Text.Encoding.UTF8.GetString(buf)
    End Function

    Private Shared Sub ReadExact(s As Stream, buf As Byte())
        Dim read = 0
        While read < buf.Length
            Dim n = s.Read(buf, read, buf.Length - read)
            If n <= 0 Then Throw New EndOfStreamException("ONNX 檔案不完整。")
            read += n
        End While
    End Sub
End Class
