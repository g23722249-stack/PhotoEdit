Imports System.IO
Imports System.Threading
Imports PhotoEdit

''' <summary>模型清單與下載：清單內容、狀態判斷、從資料夾來源下載、大小不對與取消不留壞檔、mirror 網址組合。只用暫存資料夾與小檔案。</summary>
Module ModelCatalogTests

    Sub ModelCatalogTestsRun()
        Console.WriteLine("模型清單")
        ' ---- 隨程式發佈的清單 ----
        Dim real = ModelCatalog.Load()
        Check("ModelCatalog.json 跟著程式：13 個模型", real.Models.Count = 13, real.Models.Count.ToString())
        Dim files = real.Models.SelectMany(Function(m) m.Files).ToList()
        Check("每個檔案都有路徑與大小、路徑不重複", files.All(Function(f) f.Path <> "" AndAlso f.Size > 0) AndAlso files.Select(Function(f) f.Path.ToLowerInvariant()).Distinct().Count() = files.Count)
        Check("每個模型都有名稱、用途、授權、來源", real.Models.All(Function(m) m.Name <> "" AndAlso m.Use.Length > 20 AndAlso m.License <> "" AndAlso m.Source.StartsWith("https://")))
        Dim noUrl = real.Models.Where(Function(m) m.Files.Any(Function(f) f.Url = "")).Select(Function(m) m.Id).ToList()
        Check("沒有公開位址的只有兩個自己轉檔的 UNet", noUrl.SequenceEqual({"ghibli-unet", "anything-unet"}), String.Join(",", noUrl))
        Check("用到的模型檔都在清單裡（人臉、網格、特徵點、去背）",
              {FaceDetector.ModelFile, FaceMesh.ModelFile, FaceLandmarks.ModelFile, CutoutModels.FileName(CutoutModel.General), CutoutModels.FileName(CutoutModel.Human), CutoutModels.FileName(CutoutModel.BiRefNetLite), CutoutModels.FileName(CutoutModel.ModNet), CutoutModels.FileName(CutoutModel.BiRefNetHR)}.All(Function(n) files.Any(Function(f) f.Path = n)))

        ' ---- 暫存資料夾：狀態與下載 ----
        Dim root = Path.Combine(Path.GetTempPath(), "PhotoEditModelTest_" & Guid.NewGuid().ToString("N"))
        Dim modelsDir = Path.Combine(root, "Models"), mirrorDir = Path.Combine(root, "mirror")
        Directory.CreateDirectory(Path.Combine(mirrorDir, "pack", "sub"))
        Try
            File.WriteAllBytes(Path.Combine(mirrorDir, "a.onnx"), New Byte(999) {})
            File.WriteAllBytes(Path.Combine(mirrorDir, "pack", "sub", "b.bin"), Enumerable.Range(0, 2500).Select(Function(i) CByte(i Mod 251)).ToArray())
            Dim c As New ModelCatalog With {.ModelsDir = modelsDir}
            Dim one As New ModelEntry With {.Id = "one", .Name = "一", .Files = New List(Of ModelFile) From {New ModelFile With {.Path = "a.onnx", .Size = 1000}}}
            Dim two As New ModelEntry With {.Id = "two", .Name = "二", .Files = New List(Of ModelFile) From {
                New ModelFile With {.Path = "a.onnx", .Size = 1000}, New ModelFile With {.Path = "pack/sub/b.bin", .Size = 2500}}}
            c.Models.AddRange({one, two})
            Check("還沒下載：未安裝、沒有位址不能下載", c.State(one) = ModelState.Missing AndAlso Not c.CanDownload(one))
            c.Mirror = mirrorDir
            Check("設了 mirror 資料夾：可以下載", c.CanDownload(two) AndAlso c.RemainingSize(two) = 3500)
            Dim last As Long = 0
            c.DownloadAsync(one, Sub(d, t) last = d, CancellationToken.None).Wait()
            Check("下載一：檔案到位、已安裝、進度到 1000", c.State(one) = ModelState.Installed AndAlso last = 1000)
            Check("二只剩 b.bin：不完整、還要 2500", c.State(two) = ModelState.Incomplete AndAlso c.RemainingSize(two) = 2500)
            c.DownloadAsync(two, Nothing, CancellationToken.None).Wait()
            Dim b = File.ReadAllBytes(Path.Combine(modelsDir, "pack", "sub", "b.bin"))
            Check("下載二：子資料夾自動建立、內容一樣", c.State(two) = ModelState.Installed AndAlso b.Length = 2500 AndAlso b(2499) = CByte(2499 Mod 251))
            ' 大小不對：不採用、不留暫存檔
            Dim bad As New ModelEntry With {.Id = "bad", .Files = New List(Of ModelFile) From {New ModelFile With {.Path = "pack/sub/b.bin", .Size = 9999}}}
            File.Delete(Path.Combine(modelsDir, "pack", "sub", "b.bin"))
            Dim err As Exception = Nothing
            Try
                c.DownloadAsync(bad, Nothing, CancellationToken.None).Wait()
            Catch ex As AggregateException
                err = ex.GetBaseException()
            End Try
            Check("大小不對：報錯、不留下檔案與 .download", TypeOf err Is IOException AndAlso Not File.Exists(Path.Combine(modelsDir, "pack", "sub", "b.bin")) AndAlso
                  Not File.Exists(Path.Combine(modelsDir, "pack", "sub", "b.bin.download")), err?.Message)
            ' 取消
            Using cts As New CancellationTokenSource()
                cts.Cancel()
                Dim canceled = False
                Try
                    c.DownloadAsync(two, Nothing, cts.Token).Wait()
                Catch ex As AggregateException
                    canceled = TypeOf ex.GetBaseException() Is OperationCanceledException
                End Try
                Check("取消：不留下半個檔案", canceled AndAlso Not File.Exists(Path.Combine(modelsDir, "pack", "sub", "b.bin")) AndAlso
                      Not File.Exists(Path.Combine(modelsDir, "pack", "sub", "b.bin.download")))
            End Using
            ' 刪除
            c.Delete(one)
            Check("刪除：檔案移除、變回未安裝", c.State(one) = ModelState.Missing)
            ' mirror 網址
            c.Mirror = "https://example.com/models/"
            Check("mirror 網址：網址＋相對路徑（斜線）", c.SourceOf(two.Files(1)) = "https://example.com/models/pack/sub/b.bin", c.SourceOf(two.Files(1)))
            c.Mirror = ""
            two.Files(1).Url = "https://host/x.bin"
            Check("沒有 mirror：用檔案自己的網址", c.SourceOf(two.Files(1)) = "https://host/x.bin")
            Check("大小的文字", ModelCatalog.SizeText(232589) = "227 KB" AndAlso ModelCatalog.SizeText(178648008) = "170.4 MB" AndAlso ModelCatalog.SizeText(1720147902) = "1.6 GB",
                  $"{ModelCatalog.SizeText(232589)} {ModelCatalog.SizeText(178648008)} {ModelCatalog.SizeText(1720147902)}")
        Finally
            Try
                Directory.Delete(root, True)
            Catch ex As IOException
            End Try
        End Try
    End Sub
End Module
