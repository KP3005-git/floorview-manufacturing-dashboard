-- Parameters are supplied by ReportExport.cs. No SQL string interpolation.
-- Source contract: one record per production date, shift and line.
SELECT ProductionDate, Shift, LineName, UnitsCompleted, FirstPassUnits
FROM dbo.ProductionDaily
WHERE ProductionDate >= @From
  AND ProductionDate < @ToExclusive
  AND (@Shift = 0 OR Shift = @Shift)
ORDER BY ProductionDate, Shift, LineName;
