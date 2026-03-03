IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE Name = 'Portway')
EXECUTE ('CREATE SCHEMA Portway;');
GO

IF OBJECT_ID('Portway.CompanyDivisions') IS NULL
CREATE TABLE Portway.CompanyDivisions (
    DivisionId INT PRIMARY KEY,
    DivisionName NVARCHAR(100) NOT NULL,
    ParentDivisionId INT NULL,
    DivisionHeadId INT,
    DivisionHeadName NVARCHAR(100),
    DepartmentCount INT,
    EmployeeCount INT,
    Location NVARCHAR(100),
    Region NVARCHAR(100),
    Budget DECIMAL(18,2),
    StrategicFocus NVARCHAR(200),
    CreatedDate DATETIME DEFAULT GETDATE(),
    ModifiedDate DATETIME DEFAULT GETDATE(),
    IsActive BIT NOT NULL,
    LastReviewDate DATE,
    OwnerId INT
);
GO

IF NOT EXISTS (SELECT 1 FROM Portway.CompanyDivisions)
-- Insert mockup division records
INSERT INTO Portway.CompanyDivisions
(DivisionId, DivisionName, ParentDivisionId, DivisionHeadId, DivisionHeadName, DepartmentCount, EmployeeCount, Location, Region, Budget, StrategicFocus, IsActive, LastReviewDate, OwnerId)
VALUES
(10, 'Technology', NULL, 1005, 'James Carter', 5, 120, 'New York HQ', 'North America', 2500000.00, 'Product innovation and digital platforms', 1, '2024-01-15', 501),
(20, 'Operations', NULL, 1004, 'Sophia Patel', 3, 60, 'Chicago Office', 'North America', 1500000.00, 'Process optimization and compliance', 1, '2023-12-10', 502),
(30, 'Sales & Marketing', NULL, 1006, 'Michael Brown', 4, 85, 'San Francisco Office', 'North America', 1800000.00, 'Market expansion and customer acquisition', 1, '2024-02-01', 503),
(40, 'Finance', NULL, 1007, 'Laura Chen', 2, 45, 'New York HQ', 'North America', 1200000.00, 'Financial planning and risk management', 1, '2023-11-20', 504),
(50, 'Research & Development', 10, 1008, 'Daniel Kim', 3, 35, 'Boston Lab', 'North America', 2000000.00, 'New product research and emerging tech', 1, '2023-12-18', 505);
GO
