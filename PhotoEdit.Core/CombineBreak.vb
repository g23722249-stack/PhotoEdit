Imports System.Drawing

''' <summary>
''' CorelDRAW 的「結合」與「打散」：
''' 結合（Ctrl+L）把幾個向量圖形轉成曲線後放進同一個鋼筆路徑，樣式用最下面的圖形；填色用奇偶規則，
''' 重疊的地方變成洞（字母的洞、甜甜圈、挖空的圖案）。和焊接不同，原本每條路徑的節點都原封不動保留。
''' 打散（Ctrl+K）反過來：一個有好幾條路徑的圖形拆成每條一個圖形，樣式不變。
''' </summary>
Public NotInheritable Class CombineBreak
    Private Sub New()
    End Sub

    ''' <summary>能不能參加結合：轉得成曲線的向量圖形（文字、圖說、點陣、填滿圖層、油漆桶、漸層不行）。</summary>
    Public Shared Function CanCombine(d As DrawLayer) As Boolean
        If d Is Nothing OrElse DrawGeometry.HasText(d.Shape) Then Return False
        Dim p = DrawGeometry.ToPath(d)
        Return p IsNot Nothing AndAlso p.Contours IsNot Nothing AndAlso p.Contours.Any(Function(c) c.Nodes.Count >= 2)
    End Function

    ''' <summary>能不能打散：轉成曲線後有兩條以上的路徑。</summary>
    Public Shared Function CanBreakApart(d As DrawLayer) As Boolean
        If d Is Nothing OrElse DrawGeometry.HasText(d.Shape) Then Return False
        If d.Shape = DrawShape.Path Then Return d.Contours IsNot Nothing AndAlso d.Contours.Count >= 2
        Dim p = DrawGeometry.ToPath(d)
        Return p IsNot Nothing AndAlso p.Contours IsNot Nothing AndAlso p.Contours.Count >= 2
    End Function

    ''' <summary>
    ''' 結合（layers 由下而上）：回傳一個鋼筆路徑圖層，樣式用最下面的圖形（同 CorelDRAW 框選後結合）。
    ''' 每條封閉路徑依套疊深度調整方向（外框逆時針、洞順時針），用非零環繞數判斷的地方（焊接、點選）也看得到洞。
    ''' 少於兩個能轉曲線的圖形時回傳 Nothing。
    ''' </summary>
    Public Shared Function Combine(layers As IList(Of DrawLayer)) As DrawLayer
        Dim paths = layers.Select(Function(d) If(d Is Nothing OrElse DrawGeometry.HasText(d.Shape), Nothing, DrawGeometry.ToPath(d))).ToList()
        If paths.Where(Function(p) p IsNot Nothing AndAlso p.Contours IsNot Nothing AndAlso p.Contours.Count > 0).Count() < 2 Then Return Nothing
        Dim first = paths.First(Function(p) p IsNot Nothing)
        Dim r = first.Clone()
        r.Contours = paths.Where(Function(p) p IsNot Nothing AndAlso p.Contours IsNot Nothing).
                           SelectMany(Function(p) p.Contours).Where(Function(c) c.Nodes.Count >= 2).Select(Function(c) c.Clone()).ToList()
        r.EvenOdd = True ' Id 沿用最下面的圖形：在圖層順序裡取代它的位置
        OrientByDepth(r.Contours)
        Return r
    End Function

    ''' <summary>打散：每條路徑一個圖形（樣式、名稱沿用，名稱後面加編號）。不能打散時回傳 Nothing。</summary>
    Public Shared Function BreakApart(d As DrawLayer) As List(Of DrawLayer)
        If Not CanBreakApart(d) Then Return Nothing
        Dim p = If(d.Shape = DrawShape.Path, d, DrawGeometry.ToPath(d))
        Dim result As New List(Of DrawLayer)()
        Dim k = 0
        For Each c In p.Contours
            If c.Nodes.Count < 2 Then Continue For
            k += 1
            Dim part = p.Clone()
            part.Contours = New List(Of PathContour) From {c.Clone()}
            part.EvenOdd = False
            part.Id = If(k = 1, d.Id, Nothing) ' 第一塊沿用原本的位置，其他的由呼叫端排到它上面
            If Not String.IsNullOrEmpty(d.Name) Then part.Name = d.Name & " " & k
            ' 打散後每一塊都是正常方向（洞變成實心的一塊，同 CorelDRAW）
            If part.Contours(0).Closed AndAlso SignedArea(DrawGeometry.SampleContour(part.Contours(0))) < 0 Then Reverse(part.Contours(0))
            result.Add(part)
        Next
        Return If(result.Count >= 2, result, Nothing)
    End Function

    '---------------------------------------------------------------------

    ''' <summary>封閉路徑依被幾條其他路徑包住決定方向：偶數（外框）正向、奇數（洞）反向。</summary>
    Private Shared Sub OrientByDepth(contours As List(Of PathContour))
        Dim rings = contours.Select(Function(c) If(c.Closed, DrawGeometry.SampleContour(c), Nothing)).ToList()
        For i = 0 To contours.Count - 1
            If rings(i) Is Nothing OrElse rings(i).Length < 3 Then Continue For
            Dim probe = rings(i)(0)
            Dim depth = 0
            For j = 0 To contours.Count - 1
                If j = i OrElse rings(j) Is Nothing OrElse rings(j).Length < 3 Then Continue For
                If PathBoolean.Contains({rings(j)}, probe) Then depth += 1
            Next
            Dim positive = SignedArea(rings(i)) > 0
            If positive <> (depth Mod 2 = 0) Then Reverse(contours(i))
        Next
    End Sub

    ''' <summary>反轉路徑方向：節點倒過來，每個節點的前後把手對調。</summary>
    Private Shared Sub Reverse(c As PathContour)
        c.Nodes.Reverse()
        For Each n In c.Nodes
            Dim ix = n.InX, iy = n.InY
            n.InX = n.OutX : n.InY = n.OutY
            n.OutX = ix : n.OutY = iy
        Next
    End Sub

    Private Shared Function SignedArea(r As PointF()) As Double
        Return PathBoolean.SignedArea(r)
    End Function
End Class
