Imports PhotoEdit

''' <summary>裁切形狀與裁切過程的復原紀錄。</summary>
Module CropTests

    Sub CropTestsRun()
        Console.WriteLine("裁切形狀")
        Dim r As New EditRecipe With {.CropShape = CropShape.Ellipse}
        Check("有裁切形狀時不是原圖", Not r.IsIdentity AndAlso r.HasGeometry)
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of EditRecipe)(System.Text.Json.JsonSerializer.Serialize(r))
        Check("裁切形狀 JSON 來回轉換一致", back.CropShape = CropShape.Ellipse AndAlso back.Equals(r))

        Using src = Solid(200, 100, Color.Red)
            Using rect = ImagePipeline.Render(src, New EditRecipe())
                Check("矩形不改變透明度", rect.GetPixel(1, 1).A = 255)
            End Using
            For Each shape In {CropShape.Ellipse, CropShape.RoundRect, CropShape.Heart, CropShape.Star}
                Using out = ImagePipeline.Render(src, New EditRecipe With {.CropShape = shape})
                    Check($"{shape}：角落透明、中心不透明", out.GetPixel(0, 0).A = 0 AndAlso out.GetPixel(100, 55).A = 255,
                          $"{out.GetPixel(0, 0).A} / {out.GetPixel(100, 55).A}")
                End Using
            Next
            Using out = ImagePipeline.Render(src, New EditRecipe With {.CropShape = CropShape.Ellipse, .Crop = New CropRect(0.25, 0, 0.5, 1)})
                Check("先裁成正方形再裁圓：100×100、左右邊中點不透明", out.Width = 100 AndAlso out.Height = 100 AndAlso out.GetPixel(1, 50).A > 200)
            End Using
            Using out = ImagePipeline.Render(src, New EditRecipe With {.CropShape = CropShape.Ellipse, .Frame = PhotoFrameStyle.White})
                Check("加邊框時邊框不受形狀影響", out.GetPixel(0, 0).A = 255)
            End Using
        End Using
        Using path = ImagePipeline.CropShapePath(CropShape.Heart, New RectangleF(10, 20, 100, 80))
            Dim b = path.GetBounds()
            Check("形狀路徑撐滿指定範圍", Math.Abs(b.Left - 10) < 0.5 AndAlso Math.Abs(b.Right - 110) < 0.5 AndAlso Math.Abs(b.Top - 20) < 0.5 AndAlso Math.Abs(b.Bottom - 100) < 0.5)
        End Using

        Dim h As New EditHistory()
        Dim cur As New EditRecipe()
        h.Record(cur) : cur.Exposure = 0.5
        Dim start = h.UndoCount
        h.Record(cur.Clone(), "a") : cur.Straighten = 5
        h.Record(cur.Clone()) : cur.RotateRight()
        h.TruncateTo(start)
        Check("取消裁切：去掉裁切過程中的紀錄", h.UndoCount = 1 AndAlso Not h.CanRedo)
        Dim prev = h.Undo(cur)
        Check("之後的復原回到裁切前的上一步", prev.Exposure = 0 AndAlso Not h.CanUndo)
    End Sub
End Module
