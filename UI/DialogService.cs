namespace ZephyrsElixir.UI.Dialogs;

public sealed class DialogService
{
    #region Singleton

    private static readonly Lazy<DialogService> LazyInstance = new(() => new DialogService());
    public static DialogService Instance => LazyInstance.Value;

    private DialogService() { }

    #endregion

    #region Quick Methods - Info, Warning, Error, Success

    public void ShowInfo(string messageKey, Window? owner = null, string? titleKey = null)
        => ShowSimple(messageKey, DialogType.Info, owner, titleKey ?? "Dialog_Title_Info");

    public void ShowWarning(string messageKey, Window? owner = null, string? titleKey = null)
        => ShowSimple(messageKey, DialogType.Warning, owner, titleKey ?? "Dialog_Title_Warning");

    public void ShowError(string messageKey, Window? owner = null, string? titleKey = null)
        => ShowSimple(messageKey, DialogType.Error, owner, titleKey ?? "Dialog_Title_Error");

    public void ShowSuccess(string messageKey, Window? owner = null, string? titleKey = null)
        => ShowSimple(messageKey, DialogType.Success, owner, titleKey ?? "Dialog_Title_Success");

    private void ShowSimple(string messageKey, DialogType type, Window? owner, string titleKey)
    {
        var config = new DialogConfig
        {
            Title = GetString(titleKey),
            Message = GetString(messageKey),
            Type = type,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[] { OkButton }
        };
        Show(config);
    }

    #endregion

    #region Confirmation Dialogs

    private DialogAction ShowConfirmCore(string message, DialogType type, DialogButton cancel, DialogButton confirm, Window? owner, string? title)
    {
        var config = new DialogConfig
        {
            Title = title ?? GetString("Common_Confirm_Title"),
            Message = message,
            Type = type,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[] { cancel, confirm }
        };
        var dialog = UnifiedDialog.Create(config);
        dialog.ShowDialog();
        return dialog.Result;
    }

    public bool Confirm(string messageKey, Window? owner = null, string? titleKey = null) =>
        ShowConfirmCore(GetString(messageKey), DialogType.Question,
            new DialogButton(GetString("Common_Button_No"), DialogAction.No, ButtonStyle.Secondary),
            new DialogButton(GetString("Common_Button_Yes"), DialogAction.Yes, ButtonStyle.Primary),
            owner, titleKey is null ? null : GetString(titleKey)) == DialogAction.Yes;

    public bool ConfirmCustom(string messageKey, string confirmButtonKey, string cancelButtonKey, Window? owner = null, string? titleKey = null) =>
        ShowConfirmCore(GetString(messageKey), DialogType.Question,
            new DialogButton(GetString(cancelButtonKey), DialogAction.Cancel, ButtonStyle.Secondary),
            new DialogButton(GetString(confirmButtonKey), DialogAction.Primary, ButtonStyle.Primary),
            owner, titleKey is null ? null : GetString(titleKey)) == DialogAction.Primary;

    public bool ConfirmDirect(string message, Window? owner = null, string? title = null) =>
        ShowConfirmCore(message, DialogType.Question,
            new DialogButton(GetString("Common_Button_Cancel"), DialogAction.Cancel, ButtonStyle.Secondary),
            new DialogButton(GetString("Common_Button_OK"), DialogAction.Primary, ButtonStyle.Primary),
            owner, title) == DialogAction.Primary;

    public bool ConfirmStopOptimization(Window? owner = null) =>
        ShowConfirmCore(GetString("Dialog_StopOptimization_Description"), DialogType.Warning,
            new DialogButton(GetString("Dialog_StopOptimization_ContinueButton"), DialogAction.Cancel, ButtonStyle.Secondary),
            new DialogButton(GetString("Dialog_StopOptimization_StopButton"), DialogAction.Primary, ButtonStyle.Accent),
            owner, GetString("Dialog_StopOptimization_Title")) == DialogAction.Primary;

    #endregion

    #region Pro Required Dialogs

    public bool ShowProRequired(string featureMessageKey, Window? owner = null)
    {
        var message = $"{GetString(featureMessageKey)}\n\n{GetString("Pro_Required_Upgrade_Question")}";

        var config = new DialogConfig
        {
            Title = GetString("Pro_Required_Title"),
            Message = message,
            Type = DialogType.ProRequired,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[]
            {
                new DialogButton(GetString("Common_Button_No"), DialogAction.No, ButtonStyle.Secondary),
                new DialogButton(GetString("Dialog_Button_Upgrade"), DialogAction.Upgrade, ButtonStyle.Accent)
            }
        };

        var dialog = UnifiedDialog.Create(config);
        dialog.ShowDialog();
        return dialog.Result == DialogAction.Upgrade;
    }

    public void ShowProRequiredWithUpgrade(string featureMessageKey, Window? owner = null)
    {
        var ownerWindow = owner ?? GetActiveWindow();

        if (ShowProRequired(featureMessageKey, ownerWindow))
        {
            var licenseDialog = new LicenseDialog { Owner = ownerWindow };
            licenseDialog.ShowDialog();
        }
    }

    #endregion

    #region Custom Dialog

    public DialogAction ShowCustom(DialogConfig config)
    {
        var dialog = UnifiedDialog.Create(config);
        dialog.ShowDialog();
        return dialog.Result;
    }

    public void ShowInfoDirect(string title, string message, Window? owner = null)
    {
        var config = new DialogConfig
        {
            Title = title,
            Message = message,
            Type = DialogType.Info,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[] { OkButton }
        };

        Show(config);
    }

    public void ShowFormatted(DialogType type, string messageKey, Window? owner = null, params object[] args)
    {
        var message = string.Format(GetString(messageKey), args);

        var config = new DialogConfig
        {
            Title = GetTitleForType(type),
            Message = message,
            Type = type,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[] { OkButton }
        };

        Show(config);
    }

    #endregion

    #region Private Helpers

    private static void Show(DialogConfig config)
    {
        var dialog = UnifiedDialog.Create(config);
        dialog.ShowDialog();
    }

    private static string GetString(string key) =>
        TranslationManager.Instance[key];

    private static Window? GetActiveWindow() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ??
        Application.Current.MainWindow;

    private static string GetTitleForType(DialogType type) => type switch
    {
        DialogType.Info        => GetString("Dialog_Title_Info"),
        DialogType.Success     => GetString("Dialog_Title_Success"),
        DialogType.Warning     => GetString("Dialog_Title_Warning"),
        DialogType.Error       => GetString("Dialog_Title_Error"),
        DialogType.Question    => GetString("Common_Confirm_Title"),
        DialogType.ProRequired => GetString("Pro_Required_Title"),
        _                      => GetString("Dialog_Title_Info")
    };

    private static DialogButton OkButton =>
        new(GetString("Common_Button_OK"), DialogAction.Primary, ButtonStyle.Primary);

    #endregion

    #region Info Dialogs (License, Privacy, Changelog)

    public void ShowLicense(Window? owner = null)
        => ShowRichInfo("Info_License_Title", CreateLicenseContent(), owner);

    public void ShowPrivacy(Window? owner = null)
        => ShowRichInfo("Info_Privacy_Title", CreatePrivacyContent(), owner);

    public void ShowChangelog(Window? owner = null)
        => ShowRichInfo("Info_Changelog_Title", CreateChangelogContent(), owner);

    public bool ShowEula(Window? owner = null)
    {
        var config = new DialogConfig
        {
            Title = $"{GetString("Eula_Title")} (v{AppConfiguration.Legal.EulaVersion})",
            Type = DialogType.RichContent,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[]
            {
                new DialogButton(GetString("Eula_Decline"), DialogAction.Cancel, ButtonStyle.Secondary),
                new DialogButton(GetString("Eula_Accept"), DialogAction.Primary, ButtonStyle.Primary)
            },
            RichContent = CreateEulaContent()
        };
        var dialog = UnifiedDialog.Create(config);
        dialog.ShowDialog();
        return dialog.Result == DialogAction.Primary;
    }

    private void ShowRichInfo(string titleKey, IEnumerable<Block> content, Window? owner)
    {
        var config = new DialogConfig
        {
            Title = GetString(titleKey),
            Type = DialogType.RichContent,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[] { new DialogButton(GetString("Dialog_Button_GotIt"), DialogAction.Primary, ButtonStyle.Primary) },
            RichContent = content
        };

        Show(config);
    }

    #endregion

    #region Rich Content Factories

    private static IEnumerable<Block> CreateLicenseContent()
    {
        yield return CreateParagraph(GetString("Info_License_Copyright"));
        yield return CreateParagraph(GetString("Info_License_Permission"));
        yield return CreateParagraph(GetString("Info_License_Conditions"));
        yield return CreateParagraph(GetString("Info_License_Disclaimer"));
        yield return CreateParagraphWithLink(GetString("Info_License_MoreInfo"), AppConfiguration.Legal.TermsUrl);
    }

    private static IEnumerable<Block> CreatePrivacyContent()
    {
        yield return CreateSectionTitle(GetString("Info_Privacy_Title"));

        var points = new[]
        {
            GetString("Info_Privacy_Point1"),
            GetString("Info_Privacy_Point2"),
            GetString("Info_Privacy_Point3"),
            GetString("Info_Privacy_Point4"),
            GetString("Info_Privacy_Point5")
        };

        var list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(10, 0, 0, 10) };
        foreach (var point in points)
        {
            list.ListItems.Add(new ListItem(CreateParagraph(point)) { Margin = new Thickness(0, 2, 0, 2) });
        }
        yield return list;

        yield return CreateParagraphWithLink(GetString("Info_Privacy_MoreInfo"), AppConfiguration.Legal.PrivacyUrl);
    }

    private static IEnumerable<Block> CreateChangelogContent()
    {
        var sections = new (string TitleKey, string Icon, int ItemCount)[]
        {
            ("Info_Changelog_New", "🆕", 4),
            ("Info_Changelog_Updated", "🚀", 5),
            ("Info_Changelog_Removed", "❌", 1)
        };

        foreach (var (titleKey, icon, itemCount) in sections)
        {
            yield return CreateChangelogSection($"{icon} {GetString(titleKey)}");

            var list = new List
            {
                MarkerStyle = TextMarkerStyle.Disc,
                Margin = new Thickness(15, 0, 0, 12),
                Foreground = TextBrush
            };

            for (var i = 1; i <= itemCount; i++)
            {
                list.ListItems.Add(new ListItem(new Paragraph(new Run(GetString($"{titleKey}_{i}")))
                {
                    Margin = new Thickness(0, 2, 0, 2),
                    FontSize = 13
                }));
            }

            yield return list;
        }
    }

    private static IEnumerable<Block> CreateEulaContent()
    {
        yield return CreateParagraph(GetString("Eula_Intro"));
        yield return CreateParagraph(GetString("Eula_License"));
        yield return CreateParagraph(GetString("Eula_Restrictions"));
        yield return CreateParagraph(GetString("Eula_Community"));
        yield return CreateParagraph(GetString("Eula_Ai"));
        yield return CreateParagraph(GetString("Eula_Risks"));
        yield return CreateParagraph(GetString("Eula_Whop"));
        yield return CreateParagraph(GetString("Eula_Withdrawal"));
        yield return CreateParagraphWithLink(GetString("Eula_FullTerms"), AppConfiguration.Legal.TermsUrl);
        yield return CreateParagraphWithLink(GetString("Eula_FullPrivacy"), AppConfiguration.Legal.PrivacyUrl);
    }

    #endregion

    #region FlowDocument Helpers

    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xDC, 0xDC, 0xF0));
    private static readonly Brush TitleBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xD7, 0x00));
    private static readonly Brush LinkBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x87, 0xCE, 0xFA));

    static DialogService()
    {
        TextBrush.Freeze();
        TitleBrush.Freeze();
        LinkBrush.Freeze();
    }

    private static Paragraph CreateParagraph(string text) => new(new Run(text))
    {
        Foreground = TextBrush,
        FontSize = 14,
        Margin = new Thickness(0, 0, 0, 10),
        TextAlignment = TextAlignment.Left
    };

    private static Paragraph CreateSectionTitle(string text) => new(new Run(text))
    {
        Foreground = TitleBrush,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 12)
    };

    private static Paragraph CreateChangelogSection(string text) => new(new Run(text))
    {
        Foreground = TitleBrush,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 6)
    };

    private static Paragraph CreateParagraphWithLink(string text, string url)
    {
        var paragraph = new Paragraph { Foreground = TextBrush, FontSize = 14, Margin = new Thickness(0, 8, 0, 0) };
        paragraph.Inlines.Add(new Run(text));

        var hyperlink = new Hyperlink(new Run(url)) { NavigateUri = new Uri(url), Foreground = LinkBrush };
        hyperlink.RequestNavigate += (_, e) =>
        {
            ShellUtils.OpenUrl(e.Uri);
            e.Handled = true;
        };

        paragraph.Inlines.Add(hyperlink);
        return paragraph;
    }

    #endregion

    #region Update Dialog

    public bool ShowUpdate(UpdateInfo updateInfo, Window? owner = null)
    {
        var config = new DialogConfig
        {
            Title = GetString("Update_Title"),
            Type = DialogType.RichContent,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[]
            {
                new DialogButton(GetString("Update_Button_Exit"), DialogAction.Cancel, ButtonStyle.Secondary),
                new DialogButton(GetString("Update_Button_UpdateNow"), DialogAction.Primary, ButtonStyle.Primary)
            },
            RichContent = CreateUpdateContent(updateInfo)
        };

        var dialog = UnifiedDialog.Create(config);
        dialog.ShowDialog();
        return dialog.Result == DialogAction.Primary;
    }

    private static IEnumerable<Block> CreateUpdateContent(UpdateInfo updateInfo)
    {
        yield return CreateParagraph(string.Format(GetString("Update_NewVersion"), updateInfo.Version));
        yield return CreateParagraph(GetString("Update_RequiredNotice"));
        yield return CreateSectionTitle(GetString("Update_ReleaseNotes"));
        yield return CreateParagraph(updateInfo.ReleaseNotes);
    }

    #endregion
}
