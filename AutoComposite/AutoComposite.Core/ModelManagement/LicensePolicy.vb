''' <summary>使用者預定的用途。</summary>
Public Enum IntendedUse
    ''' <summary>個人、研究或非商業用途。</summary>
    NonCommercial
    ''' <summary>商業用途（含隨軟體散布或對外提供服務）。</summary>
    Commercial
End Enum

Public NotInheritable Class LicenseDecision
    ''' <summary>可以啟用（若 RequiresConfirmation，需使用者明確同意後才可）。</summary>
    Public Property Allowed As Boolean
    Public Property RequiresConfirmation As Boolean
    Public Property Message As String = ""
End Class

''' <summary>
''' 授權檢查：非商業授權的模型不得在商業用途下啟用；授權未知或自訂授權時要使用者閱讀後確認。
''' 這只是防呆，不是法律意見；最終以模型卡與授權全文為準。
''' </summary>
Public NotInheritable Class LicensePolicy
    Private Sub New()
    End Sub

    Private Shared ReadOnly Permissive As String() = {"mit", "apache-2.0", "bsd-2-clause", "bsd-3-clause", "bsd", "isc", "unlicense", "cc0-1.0", "cc-by-4.0", "cc-by-sa-4.0", "zlib", "mpl-2.0"}
    ''' <summary>允許商用但有使用限制（例如 OpenRAIL 的用途限制條款）。</summary>
    Private Shared ReadOnly Restricted As String() = {"openrail", "openrail++", "creativeml-openrail-m", "bigscience-openrail-m", "openrail-m"}

    Public Shared Function IsNonCommercial(license As String) As Boolean
        Dim l = Normalize(license)
        Return l.Contains("-nc") OrElse l.Contains("noncommercial") OrElse l.Contains("non-commercial") OrElse l = "cc-by-nc-4.0"
    End Function

    Public Shared Function Normalize(license As String) As String
        Return If(license, "").Trim().ToLowerInvariant()
    End Function

    ''' <summary>
    ''' 依 Hub 實際授權（若有）與清單中的預期授權判斷。hubLicense 為空時使用清單值。
    ''' </summary>
    Public Shared Function Evaluate(manifest As ModelManifest, hubLicense As String, use As IntendedUse) As LicenseDecision
        Dim lic = Normalize(If(String.IsNullOrWhiteSpace(hubLicense), manifest.License.Expected, hubLicense))
        Dim expected = Normalize(manifest.License.Expected)
        Dim mismatch = Not String.IsNullOrEmpty(hubLicense) AndAlso Not String.IsNullOrEmpty(expected) AndAlso Normalize(hubLicense) <> expected
        Dim note = If(mismatch, $"（注意：模型卡目前標示 {hubLicense}，與清單預期的 {manifest.License.Expected} 不同）", "")

        If String.IsNullOrEmpty(lic) OrElse lic = "unknown" OrElse lic = "other" Then
            Return New LicenseDecision With {.Allowed = True, .RequiresConfirmation = True,
                .Message = $"授權未標示或為自訂授權{note}。請先閱讀 {LicenseUrl(manifest)} 的授權全文，確認符合你的用途後再啟用。"}
        End If
        If IsNonCommercial(lic) OrElse Normalize(manifest.License.CommercialUse) = "forbidden" Then
            If use = IntendedUse.Commercial Then
                Return New LicenseDecision With {.Allowed = False,
                    .Message = $"此模型的授權為 {lic}（非商業授權）{note}，不能用於商業用途。若需商用，請向權利人取得授權，或改用其他模型。"}
            End If
            Return New LicenseDecision With {.Allowed = True, .RequiresConfirmation = True,
                .Message = $"此模型的授權為 {lic}（僅限非商業用途）{note}。請確認你只用於個人、研究或其他非商業用途。"}
        End If
        If Restricted.Contains(lic) Then
            Return New LicenseDecision With {.Allowed = True, .RequiresConfirmation = True,
                .Message = $"此模型的授權為 {lic}，允許商用但有用途限制條款{note}，請先閱讀授權全文。"}
        End If
        If Permissive.Contains(lic) Then
            Dim needConfirm = mismatch OrElse Normalize(manifest.License.CommercialUse) = "unknown"
            Return New LicenseDecision With {.Allowed = True, .RequiresConfirmation = needConfirm,
                .Message = $"授權：{lic}{note}。" & If(needConfirm, "清單尚未確認商用條件，請閱讀授權全文。", "")}
        End If
        Return New LicenseDecision With {.Allowed = True, .RequiresConfirmation = True,
            .Message = $"授權：{lic}{note}。程式無法自動判斷此授權的商用條件，請閱讀授權全文後確認。"}
    End Function

    Public Shared Function LicenseUrl(m As ModelManifest) As String
        If Not String.IsNullOrWhiteSpace(m.License.Url) Then Return m.License.Url
        If Not String.IsNullOrWhiteSpace(m.SourceUrl) Then Return m.SourceUrl
        Return $"https://huggingface.co/{m.Repo}"
    End Function
End Class
