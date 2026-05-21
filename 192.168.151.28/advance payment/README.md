# RFC_ZADVANCE_PAYMENT — Data Lake Pipeline

## What this does
Pulls advance payment FI documents from SAP RFC `ZADVANCE_PAYMENT_RFC`
and bulk-loads them into SQL Server table `[dbo].[ET_ZADVANCE_PAYMENT]`.

**SAP RFC → .NET Console App → SQL Server (Data Lake)**

---

## Project Structure

```
RFC_ZADVANCE_PAYMENT/
├── Program.cs                  ← Main pipeline logic
├── RFCConfig.cs                ← SAP connection factory
├── LinqHelper.cs               ← RFC table → DataTable converter
├── App.config                  ← Connection strings + SAP settings
├── RFC_ZADVANCE_PAYMENT.csproj ← Project file (.NET 4.7.2)
├── run_pipeline.bat            ← Task Scheduler wrapper
└── monitor_queries.sql         ← SSMS monitoring queries
```

---

## Setup Steps

### 1. Update App.config

```xml
<!-- SQL Server -->
<add key="..." value="Server=YOUR_SQL_SERVER;Database=YOUR_DB;..." />

<!-- SAP -->
<add key="SAP_AppServerHost" value="192.168.144.174" />
<add key="SAP_Client"        value="210" />
<add key="SAP_User"          value="POWERBI" />
<add key="SAP_Password"      value="India@123456" />
<add key="COMPANY_CODE"      value="1000" />
```

### 2. Add SAP NCo DLLs

Copy `sapnco.dll` and `sapnco_utils.dll` from your existing RFC project into a `\libs\` folder
one level up from this project, OR update the `<HintPath>` in the `.csproj`.

### 3. Build

Open in Visual Studio → Build → Release.

Or via command line:
```
msbuild RFC_ZADVANCE_PAYMENT.csproj /p:Configuration=Release
```

### 4. Run

```bash
# Default: pulls yesterday, company code from App.config
RFC_ZADVANCE_PAYMENT.exe

# Custom date range + company code
RFC_ZADVANCE_PAYMENT.exe 20250101 20250331 1000
```

### 5. Schedule (Windows Task Scheduler)

- **Program:** `C:\RFC_Pipelines\RFC_ZADVANCE_PAYMENT\RFC_ZADVANCE_PAYMENT.exe`
- **Trigger:** Daily at 02:00 AM
- **Run As:** Service account with SQL + network access

Or use the included `run_pipeline.bat` as the scheduled task action.

---

## Output Tables

| Table | Description |
|---|---|
| `[dbo].[ET_ZADVANCE_PAYMENT]` | Advance payment documents (auto-created, truncated each run) |
| `[dbo].[RFC_PIPELINE_LOG]`    | Run log: timings, row counts, success/failure (auto-created) |

### ET_ZADVANCE_PAYMENT columns (from ZADVANCE_ST)

| Field | SAP Type | Description |
|---|---|---|
| DOCUMENT_TYPE | CHAR 2 | Document Type (e.g. KZ) |
| COMPANY_CODE | CHAR 4 | Company Code |
| DOCUMENT_NUMBER | CHAR 12 | FI Document Number |
| FISCAL_YEAR | NUMC 4 | Fiscal Year |
| LINE_ITEM | NUMC 3 | Line Item Number |
| POSTING_KEY | CHAR 2 | Posting Key |
| ACCOUNT_TYPE | CHAR 1 | K=Vendor, D=Customer, S=G/L |
| SPECIAL_G_L_IND | CHAR 1 | Special G/L Indicator |
| TRANSACT_TYPE | CHAR 1 | Transaction Type |
| DEBIT_CREDIT | CHAR 1 | S=Debit, H=Credit |
| AMOUNT_IN_LC | CURR 23,2 | Amount in Local Currency |
| AMOUNT | CURR 23,2 | Amount in Document Currency |
| TEXT | CHAR 50 | Item Text |
| VENDOR | CHAR 10 | Vendor Account Number |
| PAYMENT_AMT | CURR 23,2 | Net Payment Amount |
| POSTING_DATE | DATS 8 | Posting Date (YYYYMMDD) |
| SQL_DATE | datetime | Pipeline run date (audit) |

---

## Adding More RFCs

To add a new RFC (e.g. `ZPBI_PO_DATA_NEW`):
1. Copy this project folder
2. Rename `RFC_NAME`, `SAP_TABLE`, RFC function name, and parameters in `Program.cs`
3. Build + schedule separately

Each RFC runs as its own lightweight executable — same pattern as your existing pipelines.
