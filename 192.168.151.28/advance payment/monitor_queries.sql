-- ============================================================
-- RFC Pipeline Monitor Queries
-- Run these in SSMS to monitor your data lake pulls
-- ============================================================


-- 1. Latest runs (most recent first)
SELECT TOP 50
    RFC_NAME,
    SAP_TABLE,
    CONVERT(varchar, DATA_PULLFOR_DATE, 103)    AS Pull_Date,
    CONVERT(varchar, RFC_START_TIME, 108)        AS RFC_Start,
    CONVERT(varchar, RFC_END_TIME,   108)        AS RFC_End,
    DATEDIFF(SECOND, RFC_START_TIME, RFC_END_TIME) AS RFC_Secs,
    RFC_PULL_Records,
    DATEDIFF(SECOND, SQL_START_TIME, SQL_END_TIME) AS SQL_Secs,
    SQL_PUSH_Records,
    CASE WHEN IsSuccess = 1 THEN '✓ OK' ELSE '✗ FAILED' END AS Status,
    RFC_MESSAGE,
    SQL_MESSAGE,
    LOGGED_AT
FROM [dbo].[RFC_PIPELINE_LOG]
ORDER BY LOGGED_AT DESC;


-- 2. Failed runs only
SELECT *
FROM [dbo].[RFC_PIPELINE_LOG]
WHERE IsSuccess = 0
ORDER BY LOGGED_AT DESC;


-- 3. Daily summary
SELECT
    CAST(LOGGED_AT AS date)       AS Run_Date,
    RFC_NAME,
    COUNT(*)                       AS Total_Runs,
    SUM(CASE WHEN IsSuccess=1 THEN 1 ELSE 0 END) AS Success,
    SUM(CASE WHEN IsSuccess=0 THEN 1 ELSE 0 END) AS Failed,
    SUM(SQL_PUSH_Records)          AS Total_Rows_Pushed,
    AVG(DATEDIFF(SECOND, RFC_START_TIME, SQL_END_TIME)) AS Avg_Duration_Secs
FROM [dbo].[RFC_PIPELINE_LOG]
GROUP BY CAST(LOGGED_AT AS date), RFC_NAME
ORDER BY Run_Date DESC;


-- 4. Check the advance payment data
SELECT TOP 100 * FROM [dbo].[ET_ZADVANCE_PAYMENT]
ORDER BY POSTING_DATE DESC;

-- Count by vendor
SELECT
    VENDOR,
    COUNT(*)            AS Doc_Count,
    SUM(TRY_CAST(AMOUNT_IN_LC AS decimal(18,2))) AS Total_Amount_LC
FROM [dbo].[ET_ZADVANCE_PAYMENT]
GROUP BY VENDOR
ORDER BY Total_Amount_LC DESC;

-- Count by document type
SELECT
    DOCUMENT_TYPE,
    DEBIT_CREDIT,
    COUNT(*)            AS Lines,
    SUM(TRY_CAST(AMOUNT_IN_LC AS decimal(18,2))) AS Amount_LC
FROM [dbo].[ET_ZADVANCE_PAYMENT]
GROUP BY DOCUMENT_TYPE, DEBIT_CREDIT
ORDER BY Amount_LC DESC;
