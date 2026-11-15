-- Sample data for trying out row-level data scoping (ProgramId). Not part of the package — run
-- manually against the "Sales Sample" data source's database (QueryBuilderDemoSource locally).
-- Safe to re-run: drops and recreates everything it owns.
--
-- 5 programs (ProgramId 10, 20, 30, 40, 50), 100 enrollments — exactly 20 per program — so a
-- Program-admin for ProgramId 10 should see 20 of 100 rows.
--
-- Views, and what to decide for each in the admin UI:
--   sales.vw_ProgramEnrollments   ProgramId → ProgramId        (100 rows, 20 per program)
--   sales.vw_ProgramSummary       ProgramId → ProgramId        (1 row per program, aggregated)
--   sales.vw_ProgramBudgets       ProgramId → OwningProgramId  (column name differs from the key)
--   sales.vw_Programs             Not scoped                   (lookup list everyone may see)
--   sales.vw_ProgramAuditTrail    leave Undecided              (hidden from Program-admins)
--
-- Lives in the 'sales' schema (created if missing) because the local Sales Sample data source's
-- AllowedSchemas is 'sales' (only changeable via raw SQL), so no config change is needed there. If
-- a data source has a table/view allowlist, add these five views to it in the admin UI.

IF SCHEMA_ID('sales') IS NULL EXEC('CREATE SCHEMA sales');
GO

DROP VIEW IF EXISTS sales.vw_ProgramEnrollments, sales.vw_ProgramSummary, sales.vw_ProgramBudgets,
    sales.vw_Programs, sales.vw_ProgramAuditTrail;
DROP TABLE IF EXISTS sales.ProgramEnrollments, sales.ProgramBudgets, sales.Programs;
GO

CREATE TABLE sales.Programs (
    ProgramId INT PRIMARY KEY,
    ProgramName NVARCHAR(100) NOT NULL,
    Region NVARCHAR(50) NOT NULL
);

INSERT INTO sales.Programs (ProgramId, ProgramName, Region) VALUES
(10, N'Youth Coding Academy', N'North'),
(20, N'Adult Literacy', N'South'),
(30, N'Veterans Transition', N'East'),
(40, N'Small Business Mentoring', N'West'),
(50, N'Healthcare Apprenticeship', N'North');

CREATE TABLE sales.ProgramEnrollments (
    EnrollmentId INT PRIMARY KEY,
    ProgramId INT NOT NULL REFERENCES sales.Programs(ProgramId),
    ParticipantName NVARCHAR(100) NOT NULL,
    EnrolledOn DATE NOT NULL,
    Status NVARCHAR(20) NOT NULL,
    FeeAmount DECIMAL(10,2) NOT NULL
);

-- Programs are assigned round-robin (1→10, 2→20, ... 6→10), giving exactly 20 rows each.
INSERT INTO sales.ProgramEnrollments (EnrollmentId, ProgramId, ParticipantName, EnrolledOn, Status, FeeAmount)
SELECT n,
       ((n - 1) % 5 + 1) * 10,
       CONCAT(N'Participant ', FORMAT(n, '000')),
       DATEADD(DAY, n * 3, '2025-01-01'),
       CHOOSE(n % 4 + 1, N'Active', N'Completed', N'Withdrawn', N'Pending'),
       100 + (n * 37) % 400
FROM (SELECT TOP (100) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects) AS numbers;

CREATE TABLE sales.ProgramBudgets (
    BudgetId INT PRIMARY KEY,
    OwningProgramId INT NOT NULL REFERENCES sales.Programs(ProgramId),
    FiscalYear INT NOT NULL,
    Category NVARCHAR(50) NOT NULL,
    Amount DECIMAL(12,2) NOT NULL
);

INSERT INTO sales.ProgramBudgets (BudgetId, OwningProgramId, FiscalYear, Category, Amount)
SELECT ROW_NUMBER() OVER (ORDER BY p.ProgramId, y.FiscalYear, c.Category),
       p.ProgramId, y.FiscalYear, c.Category,
       p.ProgramId * 1000 + y.FiscalYear % 100 * 10 + LEN(c.Category)
FROM sales.Programs p
CROSS JOIN (VALUES (2025), (2026)) AS y(FiscalYear)
CROSS JOIN (VALUES (N'Staff'), (N'Equipment'), (N'Outreach')) AS c(Category);
GO

CREATE VIEW sales.vw_Programs AS
SELECT ProgramId, ProgramName, Region FROM sales.Programs;
GO

CREATE VIEW sales.vw_ProgramEnrollments AS
SELECT e.EnrollmentId, e.ProgramId, p.ProgramName, e.ParticipantName, e.EnrolledOn, e.Status, e.FeeAmount
FROM sales.ProgramEnrollments e
JOIN sales.Programs p ON p.ProgramId = e.ProgramId;
GO

CREATE VIEW sales.vw_ProgramSummary AS
SELECT p.ProgramId, p.ProgramName,
       COUNT(e.EnrollmentId) AS Enrollments,
       SUM(CASE WHEN e.Status = N'Active' THEN 1 ELSE 0 END) AS ActiveEnrollments,
       CAST(ISNULL(SUM(e.FeeAmount), 0) AS DECIMAL(12,2)) AS TotalFees
FROM sales.Programs p
LEFT JOIN sales.ProgramEnrollments e ON e.ProgramId = p.ProgramId
GROUP BY p.ProgramId, p.ProgramName;
GO

CREATE VIEW sales.vw_ProgramBudgets AS
SELECT b.BudgetId, b.OwningProgramId, p.ProgramName, b.FiscalYear, b.Category, b.Amount
FROM sales.ProgramBudgets b
JOIN sales.Programs p ON p.ProgramId = b.OwningProgramId;
GO

CREATE VIEW sales.vw_ProgramAuditTrail AS
SELECT e.EnrollmentId, e.ProgramId, e.ParticipantName, e.Status,
       CONCAT(N'Record reviewed by compliance on ', CONVERT(NVARCHAR(10), DATEADD(DAY, 30, e.EnrolledOn), 23)) AS Note
FROM sales.ProgramEnrollments e;
GO
