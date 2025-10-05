IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE Name = 'Portway')
EXECUTE ('CREATE SCHEMA Portway;');
GO
IF OBJECT_ID('Portway.CompanyEmployees') IS NULL
CREATE TABLE Portway.CompanyEmployees (
    EmployeeId INT PRIMARY KEY,
    FirstName NVARCHAR(50) NOT NULL,
    LastName NVARCHAR(50) NOT NULL,
    FullName AS (FirstName + ' ' + LastName),
    Email NVARCHAR(100) NOT NULL,
    Phone NVARCHAR(20),
    MobilePhone NVARCHAR(20),
    JobTitle NVARCHAR(100) NOT NULL,
    DepartmentId INT NOT NULL,
    DepartmentName NVARCHAR(100),
    DivisionId INT,
    DivisionName NVARCHAR(100),
    ManagerId INT,
    ManagerName NVARCHAR(100),
    HireDate DATE NOT NULL,
    TerminationDate DATE NULL,
    EmploymentStatus NVARCHAR(20),
    EmployeeType NVARCHAR(20),
    Location NVARCHAR(100),
    WorkAddress NVARCHAR(200),
    BirthDate DATE,
    LinkedInProfile NVARCHAR(200),
    CreatedDate DATETIME DEFAULT GETDATE(),
    ModifiedDate DATETIME DEFAULT GETDATE(),
    IsActive BIT NOT NULL,
    LastReviewDate DATE,
    SalaryGrade NVARCHAR(10),
    OwnerId INT
);
GO
IF NOT EXISTS (SELECT 1 FROM Portway.CompanyEmployees)
-- Insert mockup employee records
INSERT INTO Portway.CompanyEmployees 
(EmployeeId, FirstName, LastName, Email, Phone, MobilePhone, JobTitle, DepartmentId, DepartmentName, DivisionId, DivisionName, ManagerId, ManagerName, HireDate, EmploymentStatus, EmployeeType, Location, WorkAddress, BirthDate, LinkedInProfile, IsActive, LastReviewDate, SalaryGrade, OwnerId)
VALUES
(1001, 'Alice', 'Nguyen', 'alice.nguyen@company.com', '555-1001', '555-9001', 'Software Engineer', 201, 'Engineering', 10, 'Technology', 1005, 'James Carter', '2019-04-12', 'Active', 'Full-Time', 'New York HQ', '123 Main St, New York, NY', '1992-05-14', 'https://linkedin.com/in/alicenguyen', 1, '2023-11-15', 'S3', 501),
(1002, 'Brian', 'Lopez', 'brian.lopez@company.com', '555-1002', '555-9002', 'HR Specialist', 301, 'Human Resources', 20, 'Operations', 1004, 'Sophia Patel', '2020-06-01', 'Active', 'Full-Time', 'Chicago Office', '200 Lakeshore Dr, Chicago, IL', '1988-07-20', 'https://linkedin.com/in/brianlopez', 1, '2024-01-10', 'S2', 502),
(1003, 'Chloe', 'Smith', 'chloe.smith@company.com', '555-1003', NULL, 'Marketing Coordinator', 401, 'Marketing', 30, 'Sales & Marketing', 1006, 'Michael Brown', '2021-09-15', 'Active', 'Part-Time', 'San Francisco Office', '50 Market St, San Francisco, CA', '1996-11-02', 'https://linkedin.com/in/chloesmith', 1, '2024-02-01', 'S1', 503),
(1004, 'Sophia', 'Patel', 'sophia.patel@company.com', '555-1004', '555-9004', 'HR Manager', 301, 'Human Resources', 20, 'Operations', NULL, NULL, '2015-03-23', 'Active', 'Full-Time', 'Chicago Office', '200 Lakeshore Dr, Chicago, IL', '1984-09-18', 'https://linkedin.com/in/sophiapatel', 1, '2023-10-05', 'M2', 504),
(1005, 'James', 'Carter', 'james.carter@company.com', '555-1005', '555-9005', 'Engineering Manager', 201, 'Engineering', 10, 'Technology', NULL, NULL, '2012-01-10', 'Active', 'Full-Time', 'New York HQ', '123 Main St, New York, NY', '1980-02-28', 'https://linkedin.com/in/jamescarter', 1, '2023-09-22', 'M3', 505);
GO