# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 10 (Windows-only) WinForms desktop app for ICB's Unit Fund Department. The operator picks a Financial Year from a dropdown (populated from `UNIT_DIVIDEND`, descending) and either ticks "Select All" or enters one specific registration (REG_BK/REG_BR/REG_NO), then clicks Send. The app queries Oracle for unit holders who received dividends in that year, generates Income Tax Certificate PDFs (and Investment Certificate PDFs for CIP reinvestors) with QuestPDF, emails them via SMTP, and writes an Excel summary report plus a printable sent-status text log.

**This sends real emails to real unit holders.** Before running anything, check `Application:DryRun` and `Application:SkipDatabaseRecipients` in the active configuration (see Safety switches below), and the `TEST-OVERRIDE` block in `Services/EmailWorkflowService.cs`.

## Commands

```powershell
dotnet build
dotnet run --project PDFEmailServiceUFMS      # opens the WinForms window (no email is sent until Send is clicked and confirmed)
dotnet publish PDFEmailServiceUFMS -c Release # publish profile targets C:\EmailService, self-contained win-x64
```

- SDK is pinned to 10.0.100 via `global.json`; target framework is `net10.0-windows` with `UseWindowsForms`.
- There are no tests in this repository.

## Configuration and secrets

- `appsettings.json` contains placeholders for the Oracle connection string and SMTP credentials. Real values live in `appsettings.Production.json` (gitignored — never commit it) or environment variables.
- The generic host defaults to the `Production` environment when `DOTNET_ENVIRONMENT` is unset, so `appsettings.Production.json` is loaded on a plain `dotnet run`.
- Options classes in `Configuration/` bind to the `Email`, `RetryPolicy`, and `Application` sections.

### Safety switches (`Application` section)

- `DryRun: true` — full pipeline runs (queries, PDFs, Excel report) but no email is sent.
- `SkipDatabaseRecipients: true` — in "Select All" mode, ignores database recipients; only `TestAccounts` are processed.
- `TestAccounts` — appended to the recipient list (with `CIP_FLAG='Y'`) in "Select All" mode, even in normal runs. Not used in specific-registration mode.
- `EmailBatchSize` / `BatchDelayMinutes` — throttle: pauses N minutes after every batch to avoid SMTP rate limiting.
- `CertificateLetterNo` / `CertificateLetterNoIncomeTax` / `CertificateLetterNoInvestment` — the memo number printed top-left on a certificate (`{FIN_YEAR}` is substituted). Mirrors the `CertificateLetterNo*` keys in the UFMS `Web.config`; change both together.
- **`TEST-OVERRIDE`** (code, not config) — a marked block at the top of `Services/EmailWorkflowService.cs` (search for `TEST-OVERRIDE`). When uncommented, every email is redirected to the given test address and only the given registration is processed, regardless of UI selection. Must stay commented out (`= null`) for production sends.

## Architecture

Single project, constructor-injected services registered in `Program.cs`, interfaces under `Repositories/IRepository/`. `Program.cs` builds a generic host purely as the DI/config/logging container, then runs the WinForms message loop (`Application.Run`) — the host itself is never started.

- `UI/MainForm.cs` — the window (built in code, no designer file): FIN_YEAR dropdown (loaded via `GetFinancialYearsAsync`, descending, latest preselected), "Select All" checkbox vs. REG_BK/REG_BR/REG_NO textboxes, Send button with confirmation dialog, Cancel button, and a log textbox fed by `IProgress<string>`. Each Send click creates a fresh DI scope and runs the workflow on a background thread.
- `EmailWorkflowService` — the orchestrator: takes a `WorkflowRequest` (fin year + all-or-one selection), loads recipients (full list, or a single account via `GetAccountEmailByRegistrationAsync`), loops per account generating PDFs and sending email, then exports results to `Excel/EmailReport_*.xlsx` via ClosedXML. Per-account failures are logged and counted, never fatal. Hosts the `TEST-OVERRIDE` block.
- `PdfGenerationService` — fetches the account row (`GetHolderCertificateDataAsync`) plus the per-year rule wording and challans, maps them into `Models/HolderCertificateInfo`, and hands them to `CertificatePdfBuilder`. QuestPDF's Community license is set in its static constructor. Returns `Array.Empty<byte>()` on any failure (no data, render error) — callers treat empty as "skip this attachment". The per-year lookups are cached for the life of the scope (one Send click), and the last account row is memoised so the two certificates of one holder need a single query.
- `CertificatePdfBuilder` — the layout, ported from UFMS `Models/BLL/HolderCertificatePdfBuilder.cs` (see below). `BuildViewModel` turns the data into a `Models/CertificateView`, `BuildCertificatePdf` renders it.
- `MailKitEmailService` — SMTP via MailKit, wrapped in a Polly retry pipeline (exponential backoff + jitter) that retries only transient exception types.
- `UnitFundRepository` / `OracleConnectionFactory` — raw parameterized SQL against Oracle (`UNIT_KYC`, `UNIT_DIVIDEND`, `V_UNIT_DIVIDEND_ALL`, `UNIT_PARAMETERS`, `UNIT_CHALLAN` + `UNIT_BANK_INFO`/`UNIT_BRANCH_INFO`). `ExecuteQueryAsync` returns `null` when the query yields zero rows.

### Certificate logic

- Every recipient with `NET_DIVIDENT > 0` and a plausible email gets an Income Tax Certificate. (The "Select All" query filters emails with `LIKE '%@%.%'`; the single-registration query doesn't, so a bad address surfaces as a send failure instead of silently excluding the account.)
- Recipients with `CIP_FLAG = 'Y'` (dividend reinvested as CIP units) additionally get an Investment Certificate.
- An account whose PDFs all come back empty is skipped (no email) and marked "No" in the Excel report.

### Certificate PDFs (QuestPDF)

**The layout is a port of the UFMS `unitf059v1` screen** (`D:\Work Place\ICB\UFMS`, `Models/BLL/HolderCertificatePdfBuilder.cs` + `UFMSModel.GET_HOLDER_CERTIFICATE_DATA`), so the emailed certificate is the same document the department prints from UFMS. When the design changes there, re-port it here: `Services/CertificatePdfBuilder.cs` deliberately keeps UFMS's structure, names and comments so the two files can be diffed. UFMS also has a Solvency Certificate; it is not emailed and was not ported.

- Both certificates are composed in code in `Services/CertificatePdfBuilder.cs` (the RDLC reports were removed; they exist in git history if the old layout is ever needed).
- The letterhead is drawn as real text (Bengali title, gold English title, Bengali/English address lines) beside `Assets/ICBLogo.jpg`; `Assets/signature.png` is the signing officer's signature, cropped tight to the ink (its `Width(70)` sets the whole footprint). Both images are copied to the output directory and loaded from `<BaseDirectory>/Assets/` at runtime; if missing, the logo is skipped / blank signature space is left.
- Bengali text needs a Bangla-capable font on the machine: it tries Kalpurush first, then Nirmala UI (ships with Windows 10/11), Shonar Bangla, Vrinda.
- The signing officer's name/title are constants at the top of `CertificatePdfBuilder` (`SignatoryName` / `SignatoryTitle`) — change these plus `Assets/signature.png` when the signatory changes.
- A certificate is always one A4 page: the body is wrapped in `ScaleToFit().MinHeight(...)`, so a long tax payment table shrinks the whole letter proportionally instead of spilling over, while a short letter is not blown up.
- The numbered notes below the particulars table come from `UNIT_PARAMETERS` for the financial year — `INCOME_TAX_RULE_NAME` on the tax certificate, `INVESTMENT_RULE_NAME` on the investment certificate (with the credit year, `FIN_YEAR + 1`, substituted for its `{...}` placeholder; a year with no wording stored falls back to the sentence in code). One note per line, `"(n) ..."`; a line without a marker is a wrapped continuation of the note above it. **Whatever is stored is printed verbatim, typos included.**
- The Income Tax Certificate ends with the "Tax Payment Details" table — the approved challans of the year from `UNIT_CHALLAN`, max 12 rows, numbered on from the last stored rule. A year with no challan (or an unreadable challan table) simply gets no such table; the certificate then ends at the notes.
- Both certificates carry the holder's e-TIN (`UNIT_KYC.ETIN1`) closing the name block.
- `CertificatePdfBuilder.CreateDocument` exposes the same layout as an `IDocument`, so a layout change can be rasterised with `GenerateImages()` and eyeballed without sending anything.

## Repo notes

- Runtime outputs (`PDF/`, `Excel/`, `logs/`) are created next to the executable and are gitignored.
- The root-level `rpt_portfolio_statement_as_on_selected_investor.rpt` (Crystal Reports) and `For portfolio report.sql` are reference artifacts, not part of the build.
