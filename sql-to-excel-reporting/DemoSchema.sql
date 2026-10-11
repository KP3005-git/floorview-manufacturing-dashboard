-- Run only in a dedicated demo database. This creates a demonstration table.
-- It intentionally stops if the table already exists; it never drops data.
CREATE TABLE dbo.ProductionDaily (
    ProductionDate date NOT NULL,
    Shift int NOT NULL CHECK (Shift IN (1,2)),
    LineName nvarchar(80) NOT NULL,
    UnitsCompleted int NOT NULL CHECK (UnitsCompleted >= 0),
    FirstPassUnits int NOT NULL,
    PRIMARY KEY (ProductionDate, Shift, LineName),
    CHECK (FirstPassUnits >= 0 AND FirstPassUnits <= UnitsCompleted)
);
INSERT INTO dbo.ProductionDaily
    (ProductionDate, Shift, LineName, UnitsCompleted, FirstPassUnits)
SELECT DATEADD(day,d.n,CAST('20261005' AS date)),s.n,
    CASE l.n WHEN 0 THEN N'Line A' WHEN 1 THEN N'Line B' ELSE N'Line C' END,
    45+d.n*3+l.n*5+s.n*2,
    45+d.n*3+l.n*5+s.n*2-(3+(d.n+s.n+l.n)%6)
FROM (VALUES(0),(1),(2),(3),(4),(5),(6)) d(n)
CROSS JOIN (VALUES(1),(2)) s(n)
CROSS JOIN (VALUES(0),(1),(2)) l(n);
