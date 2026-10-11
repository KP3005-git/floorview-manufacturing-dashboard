-- SQL Server demonstration: all rows are synthetic and session-local.
-- No persistent tables are created and no existing database tables are read.
-- This is an independent SQL version of the WinForms demonstration dataset.
-- The desktop application itself uses an in-memory C# dataset, not this script.
SET NOCOUNT ON;
DECLARE @ProductionDate date = '20261008';
DECLARE @Shift int = 0; -- 0 = both, 1 = first, 2 = second
DECLARE @GoalPerShift int = 70;
DECLARE @Units TABLE (
    ProductionDate date NOT NULL,
    Shift int NOT NULL,
    UnitId varchar(30) NOT NULL,
    CompletedAt datetime2(0) NOT NULL,
    FirstPass bit NOT NULL,
    FailureReason varchar(40) NOT NULL
);

;WITH Numbers AS (
    SELECT 0 AS n
    UNION ALL SELECT n + 1 FROM Numbers WHERE n < 63
), Days AS (
    SELECT dayOffset FROM (VALUES (0), (1), (2)) d(dayOffset)
), Shifts AS (
    SELECT shiftNumber FROM (VALUES (1), (2)) s(shiftNumber)
), Seed AS (
    SELECT DATEADD(day, dayOffset, CAST('20261006' AS date)) AS ProductionDate,
        dayOffset, shiftNumber, n
    FROM Numbers CROSS JOIN Days CROSS JOIN Shifts
    WHERE n < CASE WHEN shiftNumber = 1 THEN 60 ELSE 54 END + dayOffset * 2
)
INSERT INTO @Units (ProductionDate, Shift, UnitId, CompletedAt, FirstPass, FailureReason)
SELECT ProductionDate, shiftNumber,
    'DEMO-' + RIGHT('0' + CAST(MONTH(ProductionDate) AS varchar(2)), 2)
        + RIGHT('0' + CAST(DAY(ProductionDate) AS varchar(2)), 2)
        + '-' + CAST(shiftNumber AS varchar(1))
        + '-' + RIGHT('00' + CAST(n + 1 AS varchar(3)), 3),
    DATEADD(minute, n * 9,
        DATEADD(hour, CASE WHEN shiftNumber = 1 THEN 6 ELSE 16 END,
            CAST(ProductionDate AS datetime2(0)))),
    CASE WHEN n % 6 = 0 THEN 0 ELSE 1 END,
    CASE WHEN n % 6 <> 0 THEN ''
         WHEN (n / 6 + shiftNumber + dayOffset) % 3 = 0 THEN 'Leak check'
         WHEN (n / 6 + shiftNumber + dayOffset) % 3 = 1 THEN 'Sensor check'
         ELSE 'Wiring check' END
FROM Seed OPTION (MAXRECURSION 100);

-- Expected default selection: 122 completed, 101 first-pass, 21 reworked,
-- approximately 82.8% first-pass yield and 87.1% of the 140-unit goal.
SELECT COUNT(*) AS CompletedUnits,
    SUM(CASE WHEN FirstPass = 1 THEN 1 ELSE 0 END) AS FirstPassUnits,
    SUM(CASE WHEN FirstPass = 0 THEN 1 ELSE 0 END) AS ReworkedUnits,
    CAST(100.0 * SUM(CASE WHEN FirstPass = 1 THEN 1 ELSE 0 END)
        / NULLIF(COUNT(*), 0) AS decimal(5,1)) AS FirstPassYieldPercent,
    @GoalPerShift * CASE WHEN @Shift = 0 THEN 2 ELSE 1 END AS PlannedUnits,
    CAST(100.0 * COUNT(*) / NULLIF(@GoalPerShift *
        CASE WHEN @Shift = 0 THEN 2 ELSE 1 END, 0) AS decimal(5,1)) AS GoalPercent
FROM @Units
WHERE ProductionDate = @ProductionDate AND (@Shift = 0 OR Shift = @Shift);

-- ProductionDate is the shift's operational date. CompletedAt is the actual
-- timestamp: midnight does not move a second-shift record into the next shift.
SELECT DATEADD(hour, DATEDIFF(hour, 0, CompletedAt), 0) AS CompletionHour,
    COUNT(*) AS CompletedUnits
FROM @Units
WHERE ProductionDate = @ProductionDate AND (@Shift = 0 OR Shift = @Shift)
GROUP BY DATEADD(hour, DATEDIFF(hour, 0, CompletedAt), 0)
ORDER BY CompletionHour;

SELECT FailureReason, COUNT(*) AS InitialFailures
FROM @Units
WHERE ProductionDate = @ProductionDate AND (@Shift = 0 OR Shift = @Shift)
    AND FirstPass = 0
GROUP BY FailureReason
ORDER BY InitialFailures DESC, FailureReason;

SELECT ProductionDate, Shift, UnitId, CompletedAt, FirstPass, FailureReason
FROM @Units
WHERE ProductionDate = @ProductionDate AND (@Shift = 0 OR Shift = @Shift)
ORDER BY CompletedAt;
