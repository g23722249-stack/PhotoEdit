Imports PhotoEdit

''' <summary>繪圖群組：建立後成員相鄰、巢狀、解散、隱藏與鎖定（保留自己原本的狀態）、整理空群組、樹狀清單、複製與比對。</summary>
Module GroupTests

    Private Function Recipe(n As Integer) As EditRecipe
        Dim r As New EditRecipe With {.Drawings = New List(Of DrawLayer)()}
        For i = 0 To n - 1
            r.Drawings.Add(New DrawLayer With {.Shape = DrawShape.Rectangle, .Name = "L" & i, .X = i * 0.1, .Y = 0.5, .W = 0.05, .H = 0.05})
        Next
        Return r
    End Function

    Private Function Names(r As EditRecipe) As String
        Return String.Join(",", r.Drawings.Select(Function(d) d.Name))
    End Function

    Sub GroupTestsRun()
        Console.WriteLine("繪圖群組")
        ' L0..L5 由下而上；把 L1、L4 組起來 → 排在 L4 的位置（最上面的成員），L1 跟上去
        Dim r = Recipe(6)
        Dim g = DrawGroups.Create(r, {r.Drawings(1), r.Drawings(4)}, Nothing, "群組 1")
        Check("建立群組：成員排在一起、位置在最上面的成員那裡", Names(r) = "L0,L2,L3,L1,L4,L5", Names(r))
        Check("成員的 GroupId", r.Drawings.Where(Function(d) d.GroupId = g.Id).Select(Function(d) d.Name).OrderBy(Function(s) s).SequenceEqual({"L1", "L4"}))
        ' 巢狀：把群組 1 和 L5 再組起來
        Dim outer = DrawGroups.Create(r, {r.Drawings.First(Function(d) d.Name = "L1"), r.Drawings.First(Function(d) d.Name = "L5")}, Nothing, "外層")
        Check("巢狀：群組 1 放進外層、L5 直接在外層", DrawGroups.Find(r, g.Id).ParentId = outer.Id AndAlso r.Drawings.First(Function(d) d.Name = "L5").GroupId = outer.Id)
        Check("外層的成員（含裡面）有三個", DrawGroups.MemberIndices(r, outer.Id).Count = 3)
        ' 樹狀清單（由上而下）：外層 > L5、群組 1 > L4、L1；然後 L3、L2、L0
        Dim rows = DrawGroups.Rows(r)
        Dim desc = String.Join(" ", rows.Select(Function(x) New String("."c, x.Depth) & If(x.Group IsNot Nothing, "[" & x.Group.Name & "]", r.Drawings(x.LayerIndex).Name)))
        Check("樹狀清單", desc = "[外層] .L5 .[群組 1] ..L4 ..L1 L3 L2 L0", desc)
        DrawGroups.Find(r, g.Id).Collapsed = True
        rows = DrawGroups.Rows(r)
        Check("收合：成員不列出來", rows.Count = 6, rows.Count.ToString())
        DrawGroups.Find(r, g.Id).Collapsed = False
        ' 隱藏：L4 原本就隱藏，群組再顯示時仍然隱藏
        r.Drawings.First(Function(d) d.Name = "L4").Visible = False
        DrawGroups.SetVisible(r, outer.Id, False)
        Check("隱藏外層：三個成員都隱藏", DrawGroups.MemberIndices(r, outer.Id).All(Function(i) Not r.Drawings(i).Visible))
        DrawGroups.SetVisible(r, outer.Id, True)
        Check("再顯示：原本就隱藏的 L4 仍然隱藏、其他恢復", Not r.Drawings.First(Function(d) d.Name = "L4").Visible AndAlso r.Drawings.First(Function(d) d.Name = "L1").Visible AndAlso r.Drawings.First(Function(d) d.Name = "L5").Visible)
        DrawGroups.SetLocked(r, g.Id, True)
        Check("鎖定群組 1：L1 鎖定、L5 不受影響", r.Drawings.First(Function(d) d.Name = "L1").Locked AndAlso Not r.Drawings.First(Function(d) d.Name = "L5").Locked)
        DrawGroups.SetLocked(r, g.Id, False)
        Check("解鎖", Not r.Drawings.First(Function(d) d.Name = "L1").Locked)
        ' 複製、比對
        Dim c = r.Clone()
        Check("複製配方：群組一起複製、內容相同", c.Equals(r) AndAlso c.DrawGroups IsNot r.DrawGroups AndAlso c.DrawGroups.Count = 2)
        c.DrawGroups(0).Name = "改名"
        Check("改了複本的群組名稱：配方不相同（復原紀錄看得到）", Not c.Equals(r))
        ' 解散群組 1：L1、L4 回到外層，順序不變
        Dim before = Names(r)
        DrawGroups.Ungroup(r, g.Id)
        Check("解散：成員回到外層、順序不變", Names(r) = before AndAlso r.Drawings.First(Function(d) d.Name = "L4").GroupId = outer.Id AndAlso DrawGroups.Find(r, g.Id) Is Nothing)
        ' 刪掉外層所有成員 → 整理後外層也不見
        r.Drawings.RemoveAll(Function(d) d.GroupId = outer.Id)
        DrawGroups.Prune(r)
        Check("成員都刪掉：整理時刪掉空群組", r.DrawGroups.Count = 0)
        ' 空群組（新增群組）保留
        Dim empty = DrawGroups.CreateEmpty(r, Nothing, "空的")
        DrawGroups.Prune(r, empty.Id)
        Check("正在編輯的空群組不會被整理掉", DrawGroups.Find(r, empty.Id) IsNot Nothing)
        rows = DrawGroups.Rows(r)
        Check("空群組列在最上面", rows(0).Group IsNot Nothing AndAlso rows(0).Group.Id = empty.Id)
        ' 文字貼圖夾在中間：組群組後也排在一起（統一的上下順序）
        Dim r2 = Recipe(3)
        r2.Overlays = New List(Of Overlay) From {New Overlay With {.Text = "字"}}
        LayerStack.SetOrder(r2, {New LayerRef(r2.Drawings(0)), New LayerRef(r2.Overlays(0)), New LayerRef(r2.Drawings(1)), New LayerRef(r2.Drawings(2))})
        DrawGroups.Create(r2, {r2.Drawings(0), r2.Drawings(1)}, Nothing, "G")
        Dim ord = LayerStack.Order(r2).Select(Function(x) If(x.IsOverlay, "字", x.Drawing.Name))
        Check("文字夾在中間：群組成員移到一起", String.Join(",", ord) = "字,L0,L1,L2", String.Join(",", ord))
    End Sub
End Module
