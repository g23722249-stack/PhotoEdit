''' <summary>場景快照：圖層參數複本＋遮罩物件參考（遮罩採「改前先複製」，所以參考即可保存舊狀態）。</summary>
Public NotInheritable Class SceneSnapshot
    Public Property Label As String = ""
    Public Property Layers As List(Of CompositeLayer)
    Public Property BackgroundPath As String
    Public Property Background As RgbaImage
    Public Property BackgroundInfo As ImageInfo
    Public Property ForegroundMask As FloatMask
    Public Property ProtectMask As FloatMask
    Public Property ShadowReceiverMask As FloatMask
    Public Property OcclusionFeather As Double
    Public Property Target As PlacementTarget
    Public Property Guides As List(Of GuideLine)
End Class

''' <summary>
''' 復原／重做。每次完成一個修改後呼叫 <see cref="Record"/>。修改遮罩時必須先 Clone 再改（不要原地修改舊物件），
''' 快照只保存物件參考，不複製整張影像，記憶體用量只與實際改過的遮罩成正比。
''' </summary>
Public NotInheritable Class SceneHistory
    Private ReadOnly _items As New List(Of SceneSnapshot)()
    Private _index As Integer = -1

    Public Property Capacity As Integer = 60

    Public Event Changed()

    Public ReadOnly Property CanUndo As Boolean
        Get
            Return _index > 0
        End Get
    End Property

    Public ReadOnly Property CanRedo As Boolean
        Get
            Return _index >= 0 AndAlso _index < _items.Count - 1
        End Get
    End Property

    Public ReadOnly Property UndoLabel As String
        Get
            Return If(CanUndo, _items(_index).Label, "")
        End Get
    End Property

    Public ReadOnly Property RedoLabel As String
        Get
            Return If(CanRedo, _items(_index + 1).Label, "")
        End Get
    End Property

    Public Sub Clear(scene As CompositeScene, Optional label As String = "開始")
        _items.Clear()
        _index = -1
        Record(scene, label)
    End Sub

    Public Sub Record(scene As CompositeScene, label As String)
        If _index < _items.Count - 1 Then _items.RemoveRange(_index + 1, _items.Count - _index - 1)
        _items.Add(Capture(scene, label))
        While _items.Count > Capacity
            _items.RemoveAt(0)
        End While
        _index = _items.Count - 1
        RaiseEvent Changed()
    End Sub

    Public Function Undo(scene As CompositeScene) As String
        If Not CanUndo Then Return ""
        Dim label = _items(_index).Label
        _index -= 1
        Restore(scene, _items(_index))
        RaiseEvent Changed()
        Return label
    End Function

    Public Function Redo(scene As CompositeScene) As String
        If Not CanRedo Then Return ""
        _index += 1
        Restore(scene, _items(_index))
        RaiseEvent Changed()
        Return _items(_index).Label
    End Function

    Private Shared Function Capture(s As CompositeScene, label As String) As SceneSnapshot
        Return New SceneSnapshot With {
            .Label = label, .Layers = s.Layers.Select(Function(l) l.CloneParameters()).ToList(),
            .BackgroundPath = s.BackgroundPath, .Background = s.Background, .BackgroundInfo = s.BackgroundInfo,
            .ForegroundMask = s.ForegroundMask, .ProtectMask = s.ProtectMask, .ShadowReceiverMask = s.ShadowReceiverMask,
            .OcclusionFeather = s.OcclusionFeather, .Target = s.Target.Clone(),
            .Guides = s.Guides.Select(Function(g) New GuideLine With {.X1 = g.X1, .Y1 = g.Y1, .X2 = g.X2, .Y2 = g.Y2}).ToList()}
    End Function

    Private Shared Sub Restore(s As CompositeScene, snap As SceneSnapshot)
        Dim oldVersions = s.Layers.ToDictionary(Function(l) l.Id, Function(l) l.ContentVersion)
        s.Layers = snap.Layers.Select(Function(l) l.CloneParameters()).ToList()
        ' 內容版本一律往前推進，讓快取重新計算。
        For Each l In s.Layers
            Dim v As Integer
            l.ContentVersion = If(oldVersions.TryGetValue(l.Id, v), Math.Max(v, l.ContentVersion), l.ContentVersion) + 1
        Next
        s.BackgroundPath = snap.BackgroundPath
        s.Background = snap.Background
        s.BackgroundInfo = snap.BackgroundInfo
        s.ForegroundMask = snap.ForegroundMask
        s.ProtectMask = snap.ProtectMask
        s.ShadowReceiverMask = snap.ShadowReceiverMask
        s.OcclusionFeather = snap.OcclusionFeather
        s.Target = snap.Target.Clone()
        s.Guides = snap.Guides.Select(Function(g) New GuideLine With {.X1 = g.X1, .Y1 = g.Y1, .X2 = g.X2, .Y2 = g.Y2}).ToList()
        s.TouchMasks()
    End Sub
End Class
