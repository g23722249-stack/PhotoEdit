''' <summary>
''' 復原／重做紀錄。每次修改前呼叫 Record 存下「修改前」的配方。
''' 拖曳滑桿會連續觸發修改，同一個 groupKey 在 GroupWindow 內只記一筆，復原時一次退回拖曳前。
''' </summary>
Public Class EditHistory
    Public Shared ReadOnly GroupWindow As TimeSpan = TimeSpan.FromSeconds(1.5)
    Private Const MaxEntries As Integer = 200

    Private ReadOnly _undo As New List(Of EditRecipe)()
    Private ReadOnly _redo As New Stack(Of EditRecipe)()
    Private _lastKey As String
    Private _lastTime As DateTime

    Public ReadOnly Property CanUndo As Boolean
        Get
            Return _undo.Count > 0
        End Get
    End Property

    Public ReadOnly Property CanRedo As Boolean
        Get
            Return _redo.Count > 0
        End Get
    End Property

    ''' <param name="groupKey">Nothing 表示不合併（例如按鈕操作）。</param>
    ''' <param name="now">測試用；預設為目前時間。</param>
    Public Sub Record(before As EditRecipe, Optional groupKey As String = Nothing, Optional now As DateTime? = Nothing)
        Dim t = If(now, DateTime.Now)
        Dim merge = groupKey IsNot Nothing AndAlso groupKey = _lastKey AndAlso t - _lastTime < GroupWindow AndAlso _undo.Count > 0
        _lastKey = groupKey
        _lastTime = t
        If merge Then Return
        _undo.Add(before.Clone())
        If _undo.Count > MaxEntries Then _undo.RemoveAt(0)
        _redo.Clear()
    End Sub

    Public Function Undo(current As EditRecipe) As EditRecipe
        If Not CanUndo Then Return current
        Dim prev = _undo(_undo.Count - 1)
        _undo.RemoveAt(_undo.Count - 1)
        _redo.Push(current.Clone())
        _lastKey = Nothing
        Return prev
    End Function

    Public Function Redo(current As EditRecipe) As EditRecipe
        If Not CanRedo Then Return current
        _undo.Add(current.Clone())
        _lastKey = Nothing
        Return _redo.Pop()
    End Function

    ''' <summary>可復原的步數。</summary>
    Public ReadOnly Property UndoCount As Integer
        Get
            Return _undo.Count
        End Get
    End Property

    ''' <summary>丟掉第 count 步之後的紀錄（取消裁切時，把裁切過程中的旋轉、拉直一起撤掉）。</summary>
    Public Sub TruncateTo(count As Integer)
        If count < 0 OrElse count >= _undo.Count Then Return
        _undo.RemoveRange(count, _undo.Count - count)
        _redo.Clear()
        _lastKey = Nothing
    End Sub

    Public Sub Clear()
        _undo.Clear()
        _redo.Clear()
        _lastKey = Nothing
    End Sub
End Class
