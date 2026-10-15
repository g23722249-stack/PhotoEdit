''' <summary>繪圖圖層的群組：名稱、上一層群組、顯示、鎖定、在清單上是否收合。成員是 GroupId 指向它的圖層與 ParentId 指向它的群組。</summary>
Public NotInheritable Class DrawGroup
    Public Property Id As String
    Public Property Name As String = ""
    ''' <summary>上一層群組；Nothing＝最外層。</summary>
    Public Property ParentId As String
    Public Property Visible As Boolean = True
    Public Property Locked As Boolean
    ''' <summary>清單上收合（只是顯示狀態：不存檔、不算修改、不進復原紀錄）。</summary>
    <System.Text.Json.Serialization.JsonIgnore>
    Public Property Collapsed As Boolean
    ''' <summary>PowerClip：框（群組裡最下面那個封閉圖形）的圖層 Id；群組裡其他成員只畫在框裡面。Nothing＝一般群組。</summary>
    Public Property ClipFrame As String

    Public Function Clone() As DrawGroup
        Return DirectCast(MemberwiseClone(), DrawGroup)
    End Function
End Class

''' <summary>圖層清單（樹狀）的一列：群組或圖層。</summary>
Public NotInheritable Class DrawTreeRow
    ''' <summary>群組列時為該群組，圖層列時為 Nothing。</summary>
    Public Property Group As DrawGroup
    ''' <summary>圖層列時為 Drawings 的索引，群組列時為 -1。</summary>
    Public Property LayerIndex As Integer = -1
    ''' <summary>縮排層級（0＝最外層）。</summary>
    Public Property Depth As Integer
End Class

''' <summary>
''' 群組的操作。規則：
''' 1. 圖層的 GroupId 是最內層的群組，群組的 ParentId 是上一層；
''' 2. 同一個群組（含裡面的群組）的圖層在上下順序裡一定相鄰（Normalize 會把它們排在一起，位置在最上面那個成員那裡，同 Illustrator）；
''' 3. 群組隱藏／鎖定時，成員設 Visible／Locked，並記下 HiddenByGroup／LockedByGroup，群組再顯示／解鎖時只恢復這些（自己原本就隱藏的不動），
'''    算圖、點選只看圖層自己的 Visible、Locked，不用另外查群組。
''' 字串比較用 VB 的 =（Nothing 和 Nothing 相等）。
''' </summary>
Public NotInheritable Class DrawGroups
    Private Sub New()
    End Sub

    Public Shared Function Find(r As EditRecipe, id As String) As DrawGroup
        If id Is Nothing OrElse r.DrawGroups Is Nothing Then Return Nothing
        Return r.DrawGroups.FirstOrDefault(Function(g) g.Id = id)
    End Function

    ''' <summary>圖層所在的群組，由內而外。</summary>
    Public Shared Function ChainOf(r As EditRecipe, layer As DrawLayer) As List(Of String)
        Return ChainFromGroup(r, layer.GroupId)
    End Function

    ''' <summary>groupId 自己與它的上層群組，由內而外（防止循環）。</summary>
    Public Shared Function ChainFromGroup(r As EditRecipe, groupId As String) As List(Of String)
        Dim list As New List(Of String)()
        Dim g = Find(r, groupId)
        While g IsNot Nothing AndAlso Not list.Contains(g.Id)
            list.Add(g.Id)
            g = Find(r, g.ParentId)
        End While
        Return list
    End Function

    ''' <summary>圖層是否在 groupId 裡面（任何一層）；groupId 為 Nothing 時一律是。</summary>
    Public Shared Function IsInside(r As EditRecipe, layer As DrawLayer, groupId As String) As Boolean
        Return groupId Is Nothing OrElse ChainOf(r, layer).Contains(groupId)
    End Function

    ''' <summary>
    ''' 在 parentId 這一層看，圖層屬於哪一個子群組：圖層直接在 parentId 裡時回傳 Nothing（就是圖層本身），
    ''' 在更裡面時回傳 parentId 底下那一個群組的 Id。圖層不在 parentId 裡時也回傳 Nothing。
    ''' </summary>
    Public Shared Function ChildGroupAt(r As EditRecipe, layer As DrawLayer, parentId As String) As String
        For Each id In ChainOf(r, layer)
            If Find(r, id).ParentId = parentId Then Return id
        Next
        Return Nothing
    End Function

    ''' <summary>群組裡（含裡面的群組）所有圖層的索引，由下而上。</summary>
    Public Shared Function MemberIndices(r As EditRecipe, groupId As String) As List(Of Integer)
        Dim list As New List(Of Integer)()
        If r.Drawings Is Nothing Then Return list
        For i = 0 To r.Drawings.Count - 1
            If ChainOf(r, r.Drawings(i)).Contains(groupId) Then list.Add(i)
        Next
        Return list
    End Function

    ''' <summary>
    ''' 把 layers 組成新群組（放在它們共同的那一層 parentId 底下）：直接在 parentId 裡的圖層移進新群組，
    ''' 在更裡面的，把它所在、位於 parentId 底下的那個群組移進新群組。回傳新群組。
    ''' </summary>
    Public Shared Function Create(r As EditRecipe, layers As IEnumerable(Of DrawLayer), parentId As String, name As String) As DrawGroup
        Dim g As New DrawGroup With {.Id = NewId(), .Name = name, .ParentId = parentId}
        If r.DrawGroups Is Nothing Then r.DrawGroups = New List(Of DrawGroup)()
        r.DrawGroups.Add(g)
        For Each d In layers.ToList()
            If Not IsInside(r, d, parentId) Then Continue For
            Dim child = ChildGroupAt(r, d, parentId)
            If child Is Nothing Then
                d.GroupId = g.Id
            ElseIf child <> g.Id Then
                Find(r, child).ParentId = g.Id
            End If
        Next
        Normalize(r)
        Return g
    End Function

    ''' <summary>PowerClip：框（封閉圖形）和內容組成群組，框放在群組最下面（填色在內容下、外框在內容上）。</summary>
    Public Shared Function CreateClip(r As EditRecipe, frame As DrawLayer, contents As IEnumerable(Of DrawLayer), parentId As String, name As String) As DrawGroup
        If frame.Id Is Nothing Then frame.Id = NewId()
        Dim g = Create(r, {frame}.Concat(contents), parentId, name)
        g.ClipFrame = frame.Id
        Dim order = LayerStack.Order(r)
        Dim fi = order.FindIndex(Function(x) x.Item Is frame)
        Dim first = order.FindIndex(Function(x) Not x.IsOverlay AndAlso IsInside(r, x.Drawing, g.Id))
        If first >= 0 AndAlso fi > first Then
            Dim it = order(fi)
            order.RemoveAt(fi)
            order.Insert(first, it)
            LayerStack.SetOrder(r, order)
        End If
        Return g
    End Function

    ''' <summary>建立一個空的群組（放在 parentId 底下；之後畫的圖形可以放進去）。</summary>
    Public Shared Function CreateEmpty(r As EditRecipe, parentId As String, name As String) As DrawGroup
        Dim g As New DrawGroup With {.Id = NewId(), .Name = name, .ParentId = parentId}
        If r.DrawGroups Is Nothing Then r.DrawGroups = New List(Of DrawGroup)()
        r.DrawGroups.Add(g)
        Return g
    End Function

    ''' <summary>解散群組：成員（圖層與裡面的群組）往上一層，群組刪除。上下順序不變。</summary>
    Public Shared Sub Ungroup(r As EditRecipe, groupId As String)
        Dim g = Find(r, groupId)
        If g Is Nothing Then Return
        For Each d In If(r.Drawings, New List(Of DrawLayer)())
            If d.GroupId = groupId Then d.GroupId = g.ParentId
        Next
        For Each c In r.DrawGroups
            If c.ParentId = groupId Then c.ParentId = g.ParentId
        Next
        r.DrawGroups.Remove(g)
    End Sub

    ''' <summary>群組顯示／隱藏（成員一起；見類別說明的第 3 點）。</summary>
    Public Shared Sub SetVisible(r As EditRecipe, groupId As String, visible As Boolean)
        Dim g = Find(r, groupId)
        If g Is Nothing Then Return
        g.Visible = visible
        For Each i In MemberIndices(r, groupId)
            Dim d = r.Drawings(i)
            If Not visible AndAlso d.Visible Then
                d.Visible = False : d.HiddenByGroup = True
            ElseIf visible AndAlso d.HiddenByGroup Then
                d.Visible = True : d.HiddenByGroup = False
            End If
        Next
        For Each c In r.DrawGroups.Where(Function(x) ChainFromGroup(r, x.ParentId).Contains(groupId))
            c.Visible = visible
        Next
    End Sub

    ''' <summary>群組鎖定／解鎖（成員一起）。</summary>
    Public Shared Sub SetLocked(r As EditRecipe, groupId As String, locked As Boolean)
        Dim g = Find(r, groupId)
        If g Is Nothing Then Return
        g.Locked = locked
        For Each i In MemberIndices(r, groupId)
            Dim d = r.Drawings(i)
            If locked AndAlso Not d.Locked Then
                d.Locked = True : d.LockedByGroup = True
            ElseIf Not locked AndAlso d.LockedByGroup Then
                d.Locked = False : d.LockedByGroup = False
            End If
        Next
        For Each c In r.DrawGroups.Where(Function(x) ChainFromGroup(r, x.ParentId).Contains(groupId))
            c.Locked = locked
        Next
    End Sub

    ''' <summary>
    ''' 整理：指向不存在群組的 GroupId／ParentId 清掉；沒有成員的群組刪掉（keepId 與它的上層除外，例如正在編輯的空群組）；
    ''' 再把每個群組的成員排在一起（Normalize）。
    ''' </summary>
    Public Shared Sub Prune(r As EditRecipe, Optional keepId As String = Nothing)
        If r.DrawGroups Is Nothing OrElse r.DrawGroups.Count = 0 Then
            For Each d In If(r.Drawings, New List(Of DrawLayer)())
                d.GroupId = Nothing
            Next
            Return
        End If
        Dim ids = New HashSet(Of String)(r.DrawGroups.Select(Function(g) g.Id))
        For Each g In r.DrawGroups
            If g.ParentId IsNot Nothing AndAlso Not ids.Contains(g.ParentId) Then g.ParentId = Nothing
        Next
        For Each d In If(r.Drawings, New List(Of DrawLayer)())
            If d.GroupId IsNot Nothing AndAlso Not ids.Contains(d.GroupId) Then d.GroupId = Nothing
        Next
        Dim keep = New HashSet(Of String)(ChainFromGroup(r, keepId))
        Dim changed = True
        While changed
            changed = False
            For Each g In r.DrawGroups.ToList()
                If keep.Contains(g.Id) Then Continue For
                Dim hasLayer = If(r.Drawings, New List(Of DrawLayer)()).Any(Function(d) d.GroupId = g.Id)
                Dim hasChild = r.DrawGroups.Any(Function(c) c.ParentId = g.Id)
                If Not hasLayer AndAlso Not hasChild Then
                    r.DrawGroups.Remove(g)
                    changed = True
                End If
            Next
        End While
        Normalize(r)
    End Sub

    ''' <summary>
    ''' 把每個群組的成員在上下順序裡排在一起：每一層裡的項目（圖層、群組、文字貼圖）依「最上面那個成員」的位置排序，
    ''' 群組展開成它的成員（遞迴）。原本已經相鄰時順序不變。
    ''' </summary>
    Public Shared Sub Normalize(r As EditRecipe)
        If r.DrawGroups Is Nothing OrElse r.DrawGroups.Count = 0 OrElse r.Drawings Is Nothing Then Return
        Dim order = LayerStack.Order(r)
        Dim pos As New Dictionary(Of Object, Integer)()
        For i = 0 To order.Count - 1
            pos(order(i).Item) = i
        Next
        Dim top As New Dictionary(Of String, Integer)()
        For Each lr In order
            If lr.IsOverlay Then Continue For
            For Each gid In ChainOf(r, lr.Drawing)
                Dim p = pos(lr.Item)
                Dim cur As Integer
                If Not top.TryGetValue(gid, cur) OrElse p > cur Then top(gid) = p
            Next
        Next
        Dim result As New List(Of LayerRef)()
        Dim emit As Action(Of String) = Nothing
        emit = Sub(parentId As String)
                   Dim items As New List(Of (Key As Integer, GroupId As String, Ref As LayerRef))()
                   For Each lr In order
                       If lr.IsOverlay Then
                           If parentId Is Nothing Then items.Add((pos(lr.Item), Nothing, lr))
                       ElseIf lr.Drawing.GroupId = parentId Then
                           items.Add((pos(lr.Item), Nothing, lr))
                       End If
                   Next
                   For Each g In r.DrawGroups
                       Dim k As Integer
                       If g.ParentId = parentId AndAlso top.TryGetValue(g.Id, k) Then items.Add((k, g.Id, Nothing))
                   Next
                   For Each it In items.OrderBy(Function(x) x.Key)
                       If it.GroupId Is Nothing Then result.Add(it.Ref) Else emit(it.GroupId)
                   Next
               End Sub
        emit(Nothing)
        If result.Count = order.Count AndAlso Not result.Select(Function(x) x.Item).SequenceEqual(order.Select(Function(x) x.Item)) Then
            LayerStack.SetOrder(r, result)
        End If
    End Sub

    ''' <summary>
    ''' 圖層清單的列（由上而下）：群組列後面接它的成員（縮排一層）；收合的群組不列成員。沒有成員的群組列在它那一層的最上面。
    ''' </summary>
    Public Shared Function Rows(r As EditRecipe) As List(Of DrawTreeRow)
        Dim list As New List(Of DrawTreeRow)()
        Dim drawings = If(r.Drawings, New List(Of DrawLayer)())
        If r.DrawGroups Is Nothing OrElse r.DrawGroups.Count = 0 Then
            For i = drawings.Count - 1 To 0 Step -1
                list.Add(New DrawTreeRow With {.LayerIndex = i})
            Next
            Return list
        End If
        Dim top As New Dictionary(Of String, Integer)()
        For i = 0 To drawings.Count - 1
            For Each gid In ChainOf(r, drawings(i))
                Dim cur As Integer
                If Not top.TryGetValue(gid, cur) OrElse i > cur Then top(gid) = i
            Next
        Next
        Dim emit As Action(Of String, Integer) = Nothing
        emit = Sub(parentId As String, depth As Integer)
                   Dim items As New List(Of (Key As Integer, Group As DrawGroup, Index As Integer))()
                   For i = 0 To drawings.Count - 1
                       If drawings(i).GroupId = parentId Then items.Add((i, Nothing, i))
                   Next
                   For Each g In r.DrawGroups
                       If g.ParentId <> parentId Then Continue For
                       Dim k As Integer
                       items.Add((If(top.TryGetValue(g.Id, k), k, Integer.MaxValue), g, -1))
                   Next
                   For Each it In items.OrderByDescending(Function(x) x.Key)
                       If it.Group Is Nothing Then
                           list.Add(New DrawTreeRow With {.LayerIndex = it.Index, .Depth = depth})
                       Else
                           list.Add(New DrawTreeRow With {.Group = it.Group, .Depth = depth})
                           If Not it.Group.Collapsed Then emit(it.Group.Id, depth + 1)
                       End If
                   Next
               End Sub
        emit(Nothing, 0)
        Return list
    End Function

    Private Shared Function NewId() As String
        Return "g" & Guid.NewGuid().ToString("N").Substring(0, 11)
    End Function
End Class
