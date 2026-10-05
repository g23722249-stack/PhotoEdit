Imports System.Drawing

''' <summary>圖層清單裡的一個圖層：文字／貼圖（Overlay）或繪圖圖層（Drawing），兩者只有一個不是 Nothing。</summary>
Public NotInheritable Class LayerRef
    Public ReadOnly Property Overlay As Overlay
    Public ReadOnly Property Drawing As DrawLayer

    Public Sub New(o As Overlay)
        Overlay = o
    End Sub

    Public Sub New(d As DrawLayer)
        Drawing = d
    End Sub

    Public ReadOnly Property IsOverlay As Boolean
        Get
            Return Overlay IsNot Nothing
        End Get
    End Property

    ''' <summary>圖層物件本身（比對選取用）。</summary>
    Public ReadOnly Property Item As Object
        Get
            Return If(CObj(Overlay), Drawing)
        End Get
    End Property

    Public Property Id As String
        Get
            Return If(IsOverlay, Overlay.Id, Drawing.Id)
        End Get
        Set(value As String)
            If IsOverlay Then Overlay.Id = value Else Drawing.Id = value
        End Set
    End Property

    Public Property Visible As Boolean
        Get
            Return If(IsOverlay, Overlay.Visible, Drawing.Visible)
        End Get
        Set(value As Boolean)
            If IsOverlay Then Overlay.Visible = value Else Drawing.Visible = value
        End Set
    End Property

    Public Property Opacity As Integer
        Get
            Return If(IsOverlay, Overlay.Opacity, Drawing.Opacity)
        End Get
        Set(value As Integer)
            If IsOverlay Then Overlay.Opacity = value Else Drawing.Opacity = value
        End Set
    End Property

    Public Property Blend As BlendMode
        Get
            Return If(IsOverlay, Overlay.Blend, Drawing.Blend)
        End Get
        Set(value As BlendMode)
            If IsOverlay Then Overlay.Blend = value Else Drawing.Blend = value
        End Set
    End Property

    Public ReadOnly Property Locked As Boolean
        Get
            Return Not IsOverlay AndAlso Drawing.Locked
        End Get
    End Property

    ''' <summary>清單上顯示的名稱。</summary>
    Public ReadOnly Property DisplayName As String
        Get
            If IsOverlay Then
                Select Case Overlay.Kind
                    Case OverlayKind.Text
                        Dim t = If(Overlay.Text, "").Replace(vbCrLf, " ").Replace(vbLf, " ").Trim()
                        If t.Length > 24 Then t = t.Substring(0, 24) & "…"
                        Return "文字：" & If(t = "", "（空白）", t)
                    Case OverlayKind.Image
                        Return "貼圖：" & IO.Path.GetFileNameWithoutExtension(If(Overlay.ImagePath, "").Replace("\"c, "/"c).Split("/"c).Last())
                    Case Else
                        Return "貼圖：" & Overlay.Sticker
                End Select
            End If
            Return If(String.IsNullOrEmpty(Drawing.Name), DrawGeometry.ShapeNames(CInt(Drawing.Shape)), Drawing.Name)
        End Get
    End Property

    ''' <summary>這個圖層的內容是否取決於底下的照片（「照片本身」填字、「壓暗照片」底色），不能合併進點陣圖層。</summary>
    Public ReadOnly Property DependsOnPhoto As Boolean
        Get
            Return IsOverlay AndAlso Overlay.Kind = OverlayKind.Text AndAlso
                   (Overlay.FillMode = TextFill.Photo OrElse Overlay.BackgroundStyle = TextBackground.DimPhoto)
        End Get
    End Property
End Class

''' <summary>
''' 文字、貼圖與繪圖圖層的統一堆疊：排列順序、繪製、合併。
''' 順序存在 EditRecipe.LayerOrder（圖層 Id，由下而上）；沒有排過的圖層照舊（文字貼圖在下、繪圖在上），
''' 新增的圖層（還沒有 Id 或不在清單裡）放在最上面。排序後同時把 Overlays、Drawings 兩個清單依堆疊順序重排，
''' 各分頁原本「清單索引 = 上下順序」的做法因此仍然成立。
''' </summary>
Public NotInheritable Class LayerStack
    Private Sub New()
    End Sub

    ''' <summary>由下而上的圖層。</summary>
    Public Shared Function Order(recipe As EditRecipe) As List(Of LayerRef)
        Dim all As New List(Of LayerRef)()
        For Each o In If(recipe.Overlays, New List(Of Overlay)())
            all.Add(New LayerRef(o))
        Next
        For Each d In If(recipe.Drawings, New List(Of DrawLayer)())
            all.Add(New LayerRef(d))
        Next
        If recipe.LayerOrder Is Nothing OrElse recipe.LayerOrder.Count = 0 Then Return all

        ' 同一個 Id 只認第一個（複製圖層時可能帶著原本的 Id），其餘當成新圖層。
        Dim byId As New Dictionary(Of String, LayerRef)()
        For Each r In all
            If r.Id IsNot Nothing AndAlso Not byId.ContainsKey(r.Id) Then byId(r.Id) = r
        Next
        Dim result As New List(Of LayerRef)()
        Dim used As New HashSet(Of Object)()
        For Each id In recipe.LayerOrder
            Dim r As LayerRef = Nothing
            If id IsNot Nothing AndAlso byId.TryGetValue(id, r) AndAlso used.Add(r.Item) Then result.Add(r)
        Next
        For Each r In all
            If used.Add(r.Item) Then result.Add(r)
        Next
        Return result
    End Function

    ''' <summary>
    ''' 依指定的由下而上順序寫回配方：補上缺少或重複的 Id、更新 LayerOrder，並把 Overlays、Drawings 依同樣的相對順序重排。
    ''' order 必須包含配方裡全部的圖層（可以來自 Order 再調整位置）。
    ''' </summary>
    Public Shared Sub SetOrder(recipe As EditRecipe, order As IList(Of LayerRef))
        Dim seen As New HashSet(Of String)()
        For Each r In order
            If r.Id Is Nothing OrElse Not seen.Add(r.Id) Then
                r.Id = Guid.NewGuid().ToString("N").Substring(0, 12)
                seen.Add(r.Id)
            End If
        Next
        recipe.LayerOrder = order.Select(Function(r) r.Id).ToList()
        If recipe.Overlays IsNot Nothing Then
            recipe.Overlays = order.Where(Function(r) r.IsOverlay).Select(Function(r) r.Overlay).ToList()
        End If
        If recipe.Drawings IsNot Nothing Then
            recipe.Drawings = order.Where(Function(r) Not r.IsOverlay).Select(Function(r) r.Drawing).ToList()
        End If
    End Sub

    ''' <summary>把堆疊中位置 from 的圖層移到位置 to（由下而上的索引）。</summary>
    Public Shared Sub Move(recipe As EditRecipe, from As Integer, [to] As Integer)
        Dim o = Order(recipe)
        If from < 0 OrElse from >= o.Count Then Return
        [to] = Math.Max(0, Math.Min(o.Count - 1, [to]))
        Dim item = o(from)
        o.RemoveAt(from)
        o.Insert([to], item)
        SetOrder(recipe, o)
    End Sub

    ''' <summary>圖層物件在堆疊中的位置（由下而上）；找不到時回傳 -1。</summary>
    Public Shared Function PositionOf(recipe As EditRecipe, item As Object) As Integer
        Return Order(recipe).FindIndex(Function(r) r.Item Is item)
    End Function

    ''' <summary>交換兩個圖層在堆疊中的位置。</summary>
    Public Shared Sub Swap(recipe As EditRecipe, a As Object, b As Object)
        Dim o = Order(recipe)
        Dim ia = o.FindIndex(Function(r) r.Item Is a), ib = o.FindIndex(Function(r) r.Item Is b)
        If ia < 0 OrElse ib < 0 Then Return
        Dim t = o(ia) : o(ia) = o(ib) : o(ib) = t
        SetOrder(recipe, o)
    End Sub

    ''' <summary>把 item（已加進配方）放在 anchor 正上方。</summary>
    Public Shared Sub PlaceAbove(recipe As EditRecipe, item As Object, anchor As Object)
        Dim o = Order(recipe)
        Dim i = o.FindIndex(Function(r) r.Item Is item)
        If i < 0 Then Return
        Dim moving = o(i)
        o.RemoveAt(i)
        Dim a = o.FindIndex(Function(r) r.Item Is anchor)
        o.Insert(If(a < 0, o.Count, a + 1), moving)
        SetOrder(recipe, o)
    End Sub

    '---------------------------------------------------------------------
    ' 繪製
    '---------------------------------------------------------------------

    ''' <summary>依堆疊順序畫上所有顯示中的文字、貼圖與繪圖圖層（各自的不透明度與混合模式）。</summary>
    Public Shared Sub Draw(bmp As Bitmap, recipe As EditRecipe)
        Dim layers = Order(recipe).Where(Function(r) r.Visible).ToList()
        If layers.Count = 0 Then Return
        Dim photo As Bitmap = Nothing
        If layers.Any(Function(r) r.IsOverlay AndAlso r.Overlay.Kind = OverlayKind.Text AndAlso r.Overlay.FillMode = TextFill.Photo) Then
            photo = DirectCast(bmp.Clone(), Bitmap) ' 還沒蓋上任何圖層的照片（「照片本身」填字用）
        End If
        Try
            For Each r In layers
                If r.IsOverlay Then
                    Creative.DrawOverlayOnto(bmp, r.Overlay, photo)
                Else
                    DrawingRenderer.DrawOne(bmp, r.Drawing)
                End If
            Next
        Finally
            photo?.Dispose()
        End Try
    End Sub

    '---------------------------------------------------------------------
    ' 合併
    '---------------------------------------------------------------------

    ''' <summary>不能合併的原因；可以時回傳 Nothing。</summary>
    Public Shared Function CannotMerge(layers As IEnumerable(Of LayerRef)) As String
        Dim list = layers.ToList()
        If list.Count < 2 Then Return "至少要兩個顯示中的圖層才能合併。"
        Dim locked = list.FirstOrDefault(Function(r) r.Locked)
        If locked IsNot Nothing Then Return $"「{locked.DisplayName}」已鎖定，先解除鎖定再合併。"
        Dim photo = list.FirstOrDefault(Function(r) r.DependsOnPhoto)
        If photo IsNot Nothing Then Return $"「{photo.DisplayName}」用了「照片本身」填字或「壓暗照片」底色，會隨照片改變，不能合併。"
        Return Nothing
    End Function

    ''' <summary>
    ''' 把幾個圖層（由下而上）合併成一個點陣圖層：每個圖層原封不動成為一筆（保留各自的不透明度與混合模式），
    ''' 之後只能整體移動、直接繪製或擦除。aspect 為照片寬高比（文字、貼圖算範圍用）。
    ''' </summary>
    Public Shared Function MergeIntoRaster(layers As IEnumerable(Of LayerRef), aspect As Double, name As String) As DrawLayer
        Dim ops As New List(Of DrawLayer)()
        For Each r In layers
            If r.IsOverlay Then
                Dim o = r.Overlay.Clone()
                o.Id = Nothing : o.Visible = True
                ops.Add(New DrawLayer With {.Shape = DrawShape.Raster, .Item = o, .Param1 = aspect, .Opacity = 100})
            Else
                Dim d = r.Drawing.Clone()
                d.Id = Nothing : d.Visible = True : d.Locked = False : d.Name = ""
                If d.Shape = DrawShape.Raster AndAlso d.Ops Is Nothing Then d.Ops = New List(Of DrawLayer)()
                ops.Add(d)
            End If
        Next
        Return New DrawLayer With {.Shape = DrawShape.Raster, .Name = name, .Ops = ops, .Opacity = 100}
    End Function

    ''' <summary>合併向下：位置 index 的圖層和它正下方的圖層合併。回傳新圖層；不能合併時回傳 Nothing 並給出原因。</summary>
    Public Shared Function MergeDown(recipe As EditRecipe, index As Integer, aspect As Double, ByRef reason As String) As DrawLayer
        Dim o = Order(recipe)
        If index <= 0 OrElse index >= o.Count Then reason = "下面沒有圖層可以合併。" : Return Nothing
        Dim pair = {o(index - 1), o(index)}
        If pair.Any(Function(r) Not r.Visible) Then reason = "兩個圖層都要是顯示中的才能合併。" : Return Nothing
        reason = CannotMerge(pair)
        If reason IsNot Nothing Then Return Nothing
        Dim merged = MergeIntoRaster(pair, aspect, pair(0).DisplayName)
        Replace(recipe, o, pair, merged, index - 1)
        Return merged
    End Function

    ''' <summary>合併所有顯示中的圖層（隱藏的保留不動），新圖層放在最上面那個顯示中圖層的位置。</summary>
    Public Shared Function MergeVisible(recipe As EditRecipe, aspect As Double, ByRef reason As String) As DrawLayer
        Dim o = Order(recipe)
        Dim visible = o.Where(Function(r) r.Visible).ToList()
        reason = CannotMerge(visible)
        If reason IsNot Nothing Then Return Nothing
        Dim merged = MergeIntoRaster(visible, aspect, "合併圖層")
        Dim top = o.IndexOf(visible.Last()) - (visible.Count - 1)
        Replace(recipe, o, visible, merged, top)
        Return merged
    End Function

    Private Shared Sub Replace(recipe As EditRecipe, order As List(Of LayerRef), removed As IEnumerable(Of LayerRef), merged As DrawLayer, position As Integer)
        Dim gone = New HashSet(Of Object)(removed.Select(Function(r) r.Item))
        Dim rest = order.Where(Function(r) Not gone.Contains(r.Item)).ToList()
        rest.Insert(Math.Max(0, Math.Min(rest.Count, position)), New LayerRef(merged))
        If recipe.Drawings Is Nothing Then recipe.Drawings = New List(Of DrawLayer)()
        If recipe.Overlays Is Nothing Then recipe.Overlays = New List(Of Overlay)()
        SetOrder(recipe, rest)
    End Sub
End Class
