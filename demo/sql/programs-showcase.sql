-- Richer, varied sample data for the two showcase dashboards ("Program performance", "Funnel and outcomes").
-- Run AFTER demo/sql/programs.sql, against the same database (QueryBuilderDemoSource locally; the programs database
-- on Azure). Safe to re-run: drops and recreates only what it owns. It never touches the tables and views that
-- programs.sql creates.
--
-- Everything is deterministic (no RAND/NEWID): variation comes from ids, month numbers, SIN/COS and modulo, so every
-- run gives the same numbers and every program looks different (size, seasonality, growth, budget discipline).
--
--   sales.ProgramMonthly    24 months (Jan 2025 - Dec 2026) x 5 programs
--   sales.ProgramStages     5 funnel stages per program
--   sales.ProgramSessions   12 sessions per program in 2026
--   sales.vw_ProgramMonthly / vw_ProgramFunnel / vw_ProgramSessions   all expose ProgramId (row scoping: ProgramId -> ProgramId)

IF SCHEMA_ID('sales') IS NULL EXEC('CREATE SCHEMA sales');
GO

DROP VIEW IF EXISTS sales.vw_ProgramMonthly, sales.vw_ProgramFunnel, sales.vw_ProgramSessions;
DROP TABLE IF EXISTS sales.ProgramSessions, sales.ProgramStages, sales.ProgramMonthly;
GO

CREATE TABLE sales.ProgramMonthly (
    ProgramId INT NOT NULL REFERENCES sales.Programs(ProgramId),
    MonthStart DATE NOT NULL,
    Enrolments INT NOT NULL,
    EnrolmentTarget INT NOT NULL,
    Completions INT NOT NULL,
    AttendanceRate DECIMAL(5,2) NOT NULL,
    Spend DECIMAL(12,2) NOT NULL,
    Budget DECIMAL(12,2) NOT NULL,
    CONSTRAINT PK_ProgramMonthly PRIMARY KEY (ProgramId, MonthStart)
);

CREATE TABLE sales.ProgramStages (
    ProgramId INT NOT NULL REFERENCES sales.Programs(ProgramId),
    StageOrder INT NOT NULL,
    Stage NVARCHAR(40) NOT NULL,
    Participants INT NOT NULL,
    CONSTRAINT PK_ProgramStages PRIMARY KEY (ProgramId, StageOrder)
);

CREATE TABLE sales.ProgramSessions (
    SessionId INT NOT NULL PRIMARY KEY,
    ProgramId INT NOT NULL REFERENCES sales.Programs(ProgramId),
    SessionName NVARCHAR(60) NOT NULL,
    SessionDate DATE NOT NULL,
    Attendees INT NOT NULL,
    CostPerAttendee DECIMAL(10,2) NOT NULL,
    Satisfaction DECIMAL(3,1) NOT NULL
);
GO

-- One row of shape parameters per program. Sizes, growth, seasonality (peak month), budget and how closely spend
-- follows budget all differ.
CREATE TABLE #Shape (
    ProgramId INT PRIMARY KEY,
    BaseEnrol FLOAT, Growth FLOAT, Amp FLOAT, PeakMonth INT,
    BaseBudget FLOAT, SpendBias FLOAT, AttendBase FLOAT, CompRate FLOAT,
    ScreenRate FLOAT, EnrolRate FLOAT, PlaceRate FLOAT,
    SessionSize FLOAT, FixedCost FLOAT, SatBase FLOAT
);
INSERT INTO #Shape VALUES
(10, 42, 0.025, 0.25,  9, 60000, 1.04, 88, 0.74, 0.78, 0.70, 0.62, 35, 1400, 4.2),
(20, 30, 0.005, 0.10,  1, 38000, 0.93, 79, 0.66, 0.85, 0.55, 0.40, 22,  900, 3.8),
(30, 18, 0.012, 0.30,  6, 45000, 0.98, 83, 0.81, 0.70, 0.80, 0.72, 18, 1000, 4.4),
(40, 12, 0.035, 0.15, 11, 26000, 1.10, 72, 0.58, 0.60, 0.65, 0.55, 12,  700, 3.5),
(50, 25, 0.018, 0.20,  2, 72000, 0.90, 91, 0.85, 0.55, 0.85, 0.90, 28, 2000, 4.6);

WITH n AS (
    SELECT TOP (24) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n FROM sys.all_objects
), m AS (
    SELECT s.*, n.n, DATEADD(MONTH, n.n, CAST('2025-01-01' AS DATE)) AS MonthStart
    FROM #Shape s CROSS JOIN n
), e AS (
    SELECT m.*,
           CAST(ROUND(BaseEnrol * POWER(1 + Growth, n)
                      * (1 + Amp * COS(2 * PI() * (MONTH(MonthStart) - PeakMonth) / 12.0))
                      * (1 + 0.07 * SIN(ProgramId * 0.7 + n * 1.9)), 0) AS INT) AS Enrol
    FROM m
)
INSERT INTO sales.ProgramMonthly (ProgramId, MonthStart, Enrolments, EnrolmentTarget, Completions, AttendanceRate, Spend, Budget)
SELECT ProgramId, MonthStart, Enrol,
       CAST(ROUND(BaseEnrol * 1.25 * (1 + 0.012 * n), 0) AS INT),
       CAST(ROUND(Enrol * CompRate * (0.92 + 0.08 * COS(n * 0.9 + ProgramId)), 0) AS INT),
       CAST(CASE WHEN x.Att > 99 THEN 99 ELSE x.Att END AS DECIMAL(5,2)),
       CAST(ROUND(bud.Budget * SpendBias * (1 + 0.10 * SIN(n * 0.8 + ProgramId)
                  + CASE WHEN MONTH(MonthStart) IN (9, 12) THEN 0.05 ELSE 0 END), 2) AS DECIMAL(12,2)),
       CAST(bud.Budget AS DECIMAL(12,2))
FROM e
CROSS APPLY (SELECT ROUND(AttendBase + 4 * SIN(n * 0.6 + ProgramId)
                          - CASE WHEN MONTH(MonthStart) IN (7, 12) THEN 3 ELSE 0 END, 2) AS Att) x
CROSS APPLY (SELECT ROUND(BaseBudget * (1 + CASE WHEN YEAR(MonthStart) = 2026 THEN 0.05 ELSE 0 END), 2) AS Budget) bud;

-- Stages follow from the monthly totals, so the funnel agrees with the other charts: Enrolled = total enrolments,
-- Completed = total completions.
INSERT INTO sales.ProgramStages (ProgramId, StageOrder, Stage, Participants)
SELECT s.ProgramId, st.StageOrder, st.Stage,
       CASE st.StageOrder
            WHEN 1 THEN CAST(ROUND(t.Enrolled / (s.ScreenRate * s.EnrolRate), 0) AS INT)
            WHEN 2 THEN CAST(ROUND(t.Enrolled / s.EnrolRate, 0) AS INT)
            WHEN 3 THEN t.Enrolled
            WHEN 4 THEN t.Completed
            ELSE CAST(ROUND(t.Completed * s.PlaceRate, 0) AS INT)
       END
FROM #Shape s
JOIN (SELECT ProgramId, SUM(Enrolments) AS Enrolled, SUM(Completions) AS Completed
      FROM sales.ProgramMonthly GROUP BY ProgramId) t ON t.ProgramId = s.ProgramId
CROSS JOIN (VALUES (1, N'Applied'), (2, N'Screened'), (3, N'Enrolled'), (4, N'Completed'), (5, N'Placed in work')) st(StageOrder, Stage);

-- Bigger sessions are cheaper per attendee (fixed cost spread over more people) with some noise.
WITH k AS (
    SELECT TOP (12) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS k FROM sys.all_objects
), a AS (
    SELECT s.*, k.k,
           CAST(ROUND(SessionSize * (0.45 + 1.1 * (0.5 + 0.5 * SIN(k.k * 2.1 + s.ProgramId))), 0) AS INT) AS Att
    FROM #Shape s CROSS JOIN k
)
INSERT INTO sales.ProgramSessions (SessionId, ProgramId, SessionName, SessionDate, Attendees, CostPerAttendee, Satisfaction)
SELECT ProgramId * 100 + k,
       ProgramId,
       CONCAT(N'Workshop ', k),
       DATEADD(DAY, (k - 1) * 28 + ProgramId % 7, CAST('2026-01-05' AS DATE)),
       Att,
       CAST(ROUND(FixedCost / Att + 12 + 5 * SIN(k * 1.7 + ProgramId), 2) AS DECIMAL(10,2)),
       CAST(CASE WHEN q.Sat > 5 THEN 5 WHEN q.Sat < 1 THEN 1 ELSE q.Sat END AS DECIMAL(3,1))
FROM a
CROSS APPLY (SELECT ROUND(SatBase + 0.5 * SIN(k * 1.3 + ProgramId) + 0.003 * (Att - SessionSize), 1) AS Sat) q;
GO

DROP TABLE #Shape;
GO

CREATE VIEW sales.vw_ProgramMonthly AS
SELECT m.ProgramId, p.ProgramName, p.Region, m.MonthStart,
       CAST(LEFT(CONVERT(CHAR(10), m.MonthStart, 23), 7) AS CHAR(7)) AS MonthLabel,
       YEAR(m.MonthStart) AS YearNumber,
       m.Enrolments, m.EnrolmentTarget, m.Completions, m.AttendanceRate, m.Spend, m.Budget,
       CAST(m.Budget - m.Spend AS DECIMAL(12,2)) AS Variance
FROM sales.ProgramMonthly m
JOIN sales.Programs p ON p.ProgramId = m.ProgramId;
GO

CREATE VIEW sales.vw_ProgramFunnel AS
SELECT s.ProgramId, p.ProgramName, p.Region, s.StageOrder, s.Stage, s.Participants
FROM sales.ProgramStages s
JOIN sales.Programs p ON p.ProgramId = s.ProgramId;
GO

CREATE VIEW sales.vw_ProgramSessions AS
SELECT s.SessionId, s.ProgramId, p.ProgramName, p.Region, s.SessionName, s.SessionDate,
       s.Attendees, s.CostPerAttendee, s.Satisfaction
FROM sales.ProgramSessions s
JOIN sales.Programs p ON p.ProgramId = s.ProgramId;
GO
