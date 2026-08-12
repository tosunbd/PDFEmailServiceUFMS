using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PDFEmailServiceUFMS.Configuration;
using PDFEmailServiceUFMS.Repositories.IRepository;
using PDFEmailServiceUFMS.Services;

namespace PDFEmailServiceUFMS.UI;

public class MainForm : Form
{
    private static readonly Color AccentColor = Color.FromArgb(31, 78, 121);      // ICB deep blue
    private static readonly Color AccentDark = Color.FromArgb(23, 58, 92);
    private static readonly Color PageColor = Color.FromArgb(244, 246, 249);
    private static readonly Color TextColor = Color.FromArgb(38, 44, 52);
    private static readonly Color MutedColor = Color.FromArgb(108, 117, 125);
    private static readonly Color ConsoleBack = Color.FromArgb(24, 27, 33);
    private static readonly Color ConsoleText = Color.FromArgb(204, 211, 222);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ApplicationSettings _appSettings;
    private readonly ILogger<MainForm> _logger;

    private readonly ComboBox _cboFinYear;
    private readonly CheckBox _chkSelectAll;
    private readonly Panel _pnlRegistration;
    private readonly TextBox _txtRegBk;
    private readonly TextBox _txtRegBr;
    private readonly TextBox _txtRegNo;
    private readonly Button _btnSend;
    private readonly Button _btnCancel;
    private readonly TextBox _txtLog;
    private readonly ToolStripStatusLabel _lblStatus;

    private CancellationTokenSource? _cts;

    public MainForm(
        IServiceScopeFactory scopeFactory,
        IOptions<ApplicationSettings> appSettings,
        ILogger<MainForm> logger)
    {
        _scopeFactory = scopeFactory;
        _appSettings = appSettings.Value;
        _logger = logger;

        var baseFont = new Font("Segoe UI", 9.75f);
        var inputFont = new Font("Segoe UI", 11f);

        Text = "UFMS Certificate Email Sender"
             + (_appSettings.DryRun ? "  [DRY RUN - no emails will be sent]" : "");
        Font = baseFont;
        BackColor = PageColor;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(780, 660);
        MinimumSize = new Size(760, 620);

        // ── Header ────────────────────────────────────────────────────────────
        var headerPanel = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = AccentColor };
        headerPanel.Controls.Add(new Label
        {
            Text = "Investment Corporation of Bangladesh",
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(24, 12)
        });
        headerPanel.Controls.Add(new Label
        {
            Text = "Unit Fund Department  •  Income Tax && Investment Certificate Email Sender",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(200, 219, 238),
            AutoSize = true,
            Location = new Point(26, 43)
        });

        // ── Warning banner (only when a safety switch / test override is active) ──
        var warnings = new List<string>();
        if (_appSettings.DryRun)
            warnings.Add("DRY RUN is ON — the workflow runs but NO email is sent.");
        if (_appSettings.SkipDatabaseRecipients)
            warnings.Add("SkipDatabaseRecipients is ON — only TestAccounts are processed in Select All mode.");
        if (EmailWorkflowService.TestOverrideDescription is { } overrideInfo)
            warnings.Add($"TEST-OVERRIDE ACTIVE — every email is redirected to {overrideInfo}.");

        Panel? bannerPanel = null;
        if (warnings.Count > 0)
        {
            bannerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 12 + warnings.Count * 20,
                BackColor = Color.FromArgb(255, 243, 205)
            };
            bannerPanel.Controls.Add(new Label
            {
                Text = "⚠  " + string.Join(Environment.NewLine + "⚠  ", warnings),
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(133, 100, 4),
                AutoSize = true,
                Location = new Point(24, 6)
            });
        }

        // ── Send options ──────────────────────────────────────────────────────
        var grpSend = new GroupBox
        {
            Text = "Send Options",
            Dock = DockStyle.Top,
            Height = 196,
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            ForeColor = AccentColor,
            BackColor = Color.White,
            Padding = new Padding(16, 8, 16, 12)
        };

        var lblFinYear = new Label
        {
            Text = "Financial Year:",
            Font = baseFont,
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(20, 40)
        };

        _cboFinYear = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = inputFont,
            Location = new Point(130, 34),
            Width = 190
        };

        _chkSelectAll = new CheckBox
        {
            Text = "Select All — send to EVERY recipient of the selected year, step by step",
            Font = baseFont,
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(22, 78),
            Checked = false
        };

        var lblRegHint = new Label
        {
            Text = "…or send to specific registration(s) — REG_NO accepts multiple numbers separated by commas, e.g. 27112, 50727:",
            Font = baseFont,
            ForeColor = MutedColor,
            AutoSize = true,
            Location = new Point(22, 110)
        };

        _txtRegBk = MakeRegistrationBox(inputFont, x: 88, width: 90, text: "ICB");
        _txtRegBr = MakeRegistrationBox(inputFont, x: 288, width: 70, text: "1");
        _txtRegNo = MakeRegistrationBox(inputFont, x: 468, width: 150, text: "");

        _pnlRegistration = new Panel
        {
            Location = new Point(20, 138),
            Size = new Size(620, 40),
            BackColor = Color.Transparent
        };
        _pnlRegistration.Controls.Add(MakeRegistrationLabel(baseFont, "REG_BK:", 0));
        _pnlRegistration.Controls.Add(_txtRegBk);
        _pnlRegistration.Controls.Add(MakeRegistrationLabel(baseFont, "REG_BR:", 216));
        _pnlRegistration.Controls.Add(_txtRegBr);
        _pnlRegistration.Controls.Add(MakeRegistrationLabel(baseFont, "REG_NO:", 396));
        _pnlRegistration.Controls.Add(_txtRegNo);

        grpSend.Controls.Add(lblFinYear);
        grpSend.Controls.Add(_cboFinYear);
        grpSend.Controls.Add(_chkSelectAll);
        grpSend.Controls.Add(lblRegHint);
        grpSend.Controls.Add(_pnlRegistration);

        _chkSelectAll.CheckedChanged += (_, _) => _pnlRegistration.Enabled = !_chkSelectAll.Checked;

        // ── Buttons ───────────────────────────────────────────────────────────
        var pnlButtons = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.Transparent };

        _btnSend = new Button
        {
            Text = "Send",
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            Size = new Size(160, 42),
            Location = new Point(0, 14),
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentColor,
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        _btnSend.FlatAppearance.BorderSize = 0;
        _btnSend.FlatAppearance.MouseOverBackColor = AccentDark;
        _btnSend.Click += async (_, _) => await OnSendClickedAsync();

        _btnCancel = new Button
        {
            Text = "Cancel",
            Font = baseFont,
            Size = new Size(120, 42),
            Location = new Point(174, 14),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = TextColor,
            Cursor = Cursors.Hand,
            Enabled = false
        };
        _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(190, 196, 204);
        _btnCancel.FlatAppearance.MouseOverBackColor = Color.FromArgb(233, 236, 239);
        _btnCancel.Click += (_, _) =>
        {
            _cts?.Cancel();
            AppendLog("Cancellation requested...");
        };

        pnlButtons.Controls.Add(_btnSend);
        pnlButtons.Controls.Add(_btnCancel);

        // ── Activity log ──────────────────────────────────────────────────────
        var grpLog = new GroupBox
        {
            Text = "Activity Log",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            ForeColor = AccentColor,
            BackColor = Color.White,
            Padding = new Padding(12, 6, 12, 12)
        };

        _txtLog = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = ConsoleBack,
            ForeColor = ConsoleText,
            Font = new Font("Consolas", 9.25f)
        };
        grpLog.Controls.Add(_txtLog);

        // ── Status bar ────────────────────────────────────────────────────────
        var statusStrip = new StatusStrip { BackColor = Color.White, SizingGrip = false };
        statusStrip.Items.Add(new ToolStripStatusLabel
        {
            Text = _appSettings.DryRun ? "Mode: DRY RUN" : "Mode: LIVE SEND",
            ForeColor = _appSettings.DryRun ? Color.FromArgb(176, 122, 0) : Color.FromArgb(23, 111, 44),
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold)
        });
        statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
        _lblStatus = new ToolStripStatusLabel { Text = "Ready", ForeColor = MutedColor };
        statusStrip.Items.Add(_lblStatus);

        // ── Compose (content panel first so docked header/banner/status wrap it) ──
        var contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 16), BackColor = PageColor };
        contentPanel.Controls.Add(grpLog);
        contentPanel.Controls.Add(pnlButtons);
        contentPanel.Controls.Add(grpSend);

        Controls.Add(contentPanel);
        if (bannerPanel != null)
            Controls.Add(bannerPanel);
        Controls.Add(headerPanel);
        Controls.Add(statusStrip);

        Shown += async (_, _) => await LoadFinancialYearsAsync();
    }

    private static TextBox MakeRegistrationBox(Font font, int x, int width, string text) => new()
    {
        Location = new Point(x, 4),
        Width = width,
        Text = text,
        Font = font,
        TextAlign = HorizontalAlignment.Center
    };

    private static Label MakeRegistrationLabel(Font font, string text, int x) => new()
    {
        Text = text,
        Font = font,
        ForeColor = TextColor,
        AutoSize = true,
        Location = new Point(x, 10)
    };

    private async Task LoadFinancialYearsAsync()
    {
        try
        {
            AppendLog("Loading financial years from database...");
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IUnitFundRepository>();
            var years = await repository.GetFinancialYearsAsync();

            _cboFinYear.Items.Clear();
            if (years != null)
            {
                foreach (System.Data.DataRow row in years.Rows)
                {
                    var year = row["FIN_YEAR"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(year))
                        _cboFinYear.Items.Add(year);
                }
            }

            if (_cboFinYear.Items.Count == 0)
            {
                _cboFinYear.Items.Add(_appSettings.FinancialYear);
                AppendLog($"No financial years found in UNIT_DIVIDEND - using configured fallback {_appSettings.FinancialYear}");
            }

            _cboFinYear.SelectedIndex = 0; // list is descending, so this is the latest year
            AppendLog($"Loaded {_cboFinYear.Items.Count} financial year(s). Latest: {_cboFinYear.SelectedItem}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load financial years");
            _cboFinYear.Items.Clear();
            _cboFinYear.Items.Add(_appSettings.FinancialYear);
            _cboFinYear.SelectedIndex = 0;
            AppendLog($"ERROR loading financial years: {ex.Message}");
            AppendLog($"Using configured fallback financial year {_appSettings.FinancialYear}. Check the Oracle connection.");
        }
    }

    private async Task OnSendClickedAsync()
    {
        if (_cboFinYear.SelectedItem is not string finYear || string.IsNullOrWhiteSpace(finYear))
        {
            MessageBox.Show(this, "Please select a Financial Year first.", "Missing input",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        WorkflowRequest request;
        string confirmText;

        if (_chkSelectAll.Checked)
        {
            request = new WorkflowRequest(finYear, SendToAll: true);
            confirmText = $"Send Income Tax / Investment Certificate emails to ALL unit holders " +
                          $"with dividends in {finYear}?";
        }
        else
        {
            var regBk = _txtRegBk.Text.Trim();
            var regBr = _txtRegBr.Text.Trim();
            var regNo = _txtRegNo.Text.Trim();

            var regNos = regNo
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct()
                .ToList();

            if (string.IsNullOrWhiteSpace(regBk) || string.IsNullOrWhiteSpace(regBr) || regNos.Count == 0)
            {
                MessageBox.Show(this, "Enter REG_BK, REG_BR and at least one REG_NO (comma-separated for multiple), or tick \"Select All\".",
                    "Missing input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            request = new WorkflowRequest(finYear, SendToAll: false, regBk, regBr, regNo);
            confirmText = regNos.Count == 1
                ? $"Send the certificate email for registration {regBk}/{regBr}/{regNos[0]} ({finYear})?"
                : $"Send {regNos.Count} certificate emails, one for each of these registrations ({finYear})?\n\n" +
                  $"{regBk}/{regBr}/  {string.Join(", ", regNos)}";
        }

        if (_appSettings.DryRun)
        {
            confirmText += "\n\n(DryRun is ON - no email will actually be sent.)";
        }

        var confirm = MessageBox.Show(this, confirmText, "Confirm send",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
            return;

        SetBusy(true);
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(AppendLog); // created on UI thread => reports marshal back to UI

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var workflow = scope.ServiceProvider.GetRequiredService<IEmailWorkflowService>();

            var result = await Task.Run(() => workflow.ExecuteAsync(request, progress, _cts.Token));

            MessageBox.Show(this,
                $"Finished.\n\nRecipients: {result.TotalRecipients}\nSent: {result.SuccessCount}\nFailed: {result.FailureCount}",
                "Workflow completed", MessageBoxButtons.OK,
                result.FailureCount == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Workflow cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow failed");
            AppendLog($"ERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Workflow failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _cboFinYear.Enabled = !busy;
        _chkSelectAll.Enabled = !busy;
        _pnlRegistration.Enabled = !busy && !_chkSelectAll.Checked;
        _btnSend.Enabled = !busy;
        _btnSend.Text = busy ? "Sending..." : "Send";
        _btnCancel.Enabled = busy;
        _lblStatus.Text = busy ? "Sending..." : "Ready";
    }

    private void AppendLog(string message)
    {
        _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
