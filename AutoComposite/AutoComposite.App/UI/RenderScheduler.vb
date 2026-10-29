Imports System.Threading
Imports AutoComposite

''' <summary>
''' 預覽算圖排程：在背景執行緒算（不卡介面），連續的請求只保留最新一筆；新請求到來時取消正在算的舊請求。
''' 每次都對場景快照算圖，UI 可以同時繼續修改場景。
''' </summary>
Friend NotInheritable Class RenderScheduler
    Implements IDisposable

    Private ReadOnly _owner As Control
    Private ReadOnly _renderer As ICompositeRenderer
    Private _cts As CancellationTokenSource
    Private _running As Boolean
    Private _pending As (Scene As CompositeScene, Options As RenderOptions)?
    Private _version As Integer

    ''' <summary>算好時在 UI 執行緒呼叫：影像、耗時（毫秒）。</summary>
    Public Event Rendered(image As RgbaImage, result As RenderResult)
    Public Event Failed(ex As Exception)

    Public Sub New(owner As Control, renderer As ICompositeRenderer)
        _owner = owner
        _renderer = renderer
    End Sub

    Public Sub Request(scene As CompositeScene, options As RenderOptions)
        If scene?.Background Is Nothing Then Return
        _pending = (scene.CloneForRender(), options)
        _version += 1
        If _running Then
            _cts?.Cancel()
            Return
        End If
        StartNext()
    End Sub

    Private Sub StartNext()
        If Not _pending.HasValue Then Return
        Dim job = _pending.Value
        _pending = Nothing
        _running = True
        _cts?.Dispose()
        _cts = New CancellationTokenSource()
        Dim token = _cts.Token
        Dim myVersion = _version
        Task.Run(Function() _renderer.Render(job.Scene, job.Options, token), token).ContinueWith(
            Sub(t)
                If _owner.IsDisposed Then Return
                Try
                    _owner.BeginInvoke(Sub() Completed(t, myVersion))
                Catch ex As InvalidOperationException
                End Try
            End Sub)
    End Sub

    Private Sub Completed(t As Task(Of RenderResult), version As Integer)
        _running = False
        If t.Status = TaskStatus.RanToCompletion Then
            ' 已經有更新的請求時，舊結果仍先顯示（拖曳時比較流暢），再接著算新的。
            RaiseEvent Rendered(t.Result.Image, t.Result)
        ElseIf t.IsFaulted Then
            Dim ex = t.Exception.GetBaseException()
            If Not TypeOf ex Is OperationCanceledException Then
                AppLog.Error("預覽算圖失敗", ex)
                RaiseEvent Failed(ex)
            End If
        End If
        StartNext()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        _cts?.Cancel()
        _cts?.Dispose()
    End Sub
End Class
