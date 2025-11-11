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
GO
CREATE OR ALTER PROCEDURE [Portway].[usp_ManageCompanyEmployees]
    @Method NVARCHAR(10),
    @UserName NVARCHAR(100) = NULL,
    
    -- Employee fields
    @ID INT = NULL,
    @FirstName NVARCHAR(50) = NULL,
    @LastName NVARCHAR(50) = NULL,
	@FullName NVARCHAR(100) = NULL,
    @Email NVARCHAR(100) = NULL,
    @Phone NVARCHAR(20) = NULL,
    @MobilePhone NVARCHAR(20) = NULL,
    @JobTitle NVARCHAR(100) = NULL,
    @DepartmentId INT = NULL,
    @DepartmentName NVARCHAR(100) = NULL,
    @DivisionId INT = NULL,
    @DivisionName NVARCHAR(100) = NULL,
    @ManagerId INT = NULL,
    @ManagerName NVARCHAR(100) = NULL,
    @HireDate DATE = NULL,
    @TerminationDate DATE = NULL,
    @EmploymentStatus NVARCHAR(20) = NULL,
    @EmployeeType NVARCHAR(20) = NULL,
    @Location NVARCHAR(100) = NULL,
    @WorkAddress NVARCHAR(200) = NULL,
    @BirthDate DATE = NULL,
    @LinkedInProfile NVARCHAR(200) = NULL,
    @IsActive BIT = NULL,
    @LastReviewDate DATE = NULL,
    @SalaryGrade NVARCHAR(10) = NULL,
    @OwnerId INT = NULL,
	@CreatedDate DATETIME = NULL,
	@ModifiedDate DATETIME = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now DATETIME = GETDATE();
	DECLARE @EmployeeID INT = @ID;

    -- INSERT (POST)
    IF @Method = 'INSERT'
    BEGIN
        -- Generate new EmployeeId (simple auto-increment logic)
        DECLARE @NewEmployeeId INT;
        SELECT @NewEmployeeId = ISNULL(MAX(EmployeeId), 1000) + 1 
        FROM Portway.CompanyEmployees;

        INSERT INTO Portway.CompanyEmployees (
            EmployeeId, FirstName, LastName, Email, Phone, MobilePhone, JobTitle,
            DepartmentId, DepartmentName, DivisionId, DivisionName, ManagerId, ManagerName,
            HireDate, TerminationDate, EmploymentStatus, EmployeeType, Location, WorkAddress,
            BirthDate, LinkedInProfile, CreatedDate, ModifiedDate, IsActive, 
            LastReviewDate, SalaryGrade, OwnerId
        )
        VALUES (
            @NewEmployeeId, @FirstName, @LastName, @Email, @Phone, @MobilePhone, @JobTitle,
            @DepartmentId, @DepartmentName, @DivisionId, @DivisionName, @ManagerId, @ManagerName,
            @HireDate, @TerminationDate, @EmploymentStatus, @EmployeeType, @Location, @WorkAddress,
            @BirthDate, @LinkedInProfile, @Now, @Now, ISNULL(@IsActive, 1),
            @LastReviewDate, @SalaryGrade, @OwnerId
        );

        -- Return the newly created employee
        SELECT * FROM Portway.CompanyEmployees WHERE EmployeeId = @NewEmployeeId;
    END

    -- UPDATE (PUT - full update)
    ELSE IF @Method = 'UPDATE'
    BEGIN
        IF @EmployeeId IS NULL
        BEGIN
            RAISERROR('EmployeeId is required for UPDATE operation', 16, 1);
            RETURN;
        END

        UPDATE Portway.CompanyEmployees
        SET
            FirstName = ISNULL(@FirstName, FirstName),
            LastName = ISNULL(@LastName, LastName),
            Email = ISNULL(@Email, Email),
            Phone = @Phone,
            MobilePhone = @MobilePhone,
            JobTitle = ISNULL(@JobTitle, JobTitle),
            DepartmentId = ISNULL(@DepartmentId, DepartmentId),
            DepartmentName = @DepartmentName,
            DivisionId = @DivisionId,
            DivisionName = @DivisionName,
            ManagerId = @ManagerId,
            ManagerName = @ManagerName,
            HireDate = ISNULL(@HireDate, HireDate),
            TerminationDate = @TerminationDate,
            EmploymentStatus = @EmploymentStatus,
            EmployeeType = @EmployeeType,
            Location = @Location,
            WorkAddress = @WorkAddress,
            BirthDate = @BirthDate,
            LinkedInProfile = @LinkedInProfile,
            ModifiedDate = @Now,
            IsActive = ISNULL(@IsActive, IsActive),
            LastReviewDate = @LastReviewDate,
            SalaryGrade = @SalaryGrade,
            OwnerId = @OwnerId
        WHERE EmployeeId = @EmployeeId;

        -- Return the updated employee
        SELECT * FROM Portway.CompanyEmployees WHERE EmployeeId = @EmployeeId;
    END

    -- PATCH (partial update - only update provided fields)
    ELSE IF @Method = 'PATCH'
    BEGIN
        IF @EmployeeId IS NULL
        BEGIN
            RAISERROR('EmployeeId is required for PATCH operation', 16, 1);
            RETURN;
        END

        -- Build dynamic UPDATE statement to only update provided fields
        DECLARE @SQL NVARCHAR(MAX) = N'UPDATE Portway.CompanyEmployees SET ModifiedDate = @Now';
        
        IF @FirstName IS NOT NULL SET @SQL = @SQL + N', FirstName = @FirstName';
        IF @LastName IS NOT NULL SET @SQL = @SQL + N', LastName = @LastName';
        IF @Email IS NOT NULL SET @SQL = @SQL + N', Email = @Email';
        IF @Phone IS NOT NULL SET @SQL = @SQL + N', Phone = @Phone';
        IF @MobilePhone IS NOT NULL SET @SQL = @SQL + N', MobilePhone = @MobilePhone';
        IF @JobTitle IS NOT NULL SET @SQL = @SQL + N', JobTitle = @JobTitle';
        IF @DepartmentId IS NOT NULL SET @SQL = @SQL + N', DepartmentId = @DepartmentId';
        IF @DepartmentName IS NOT NULL SET @SQL = @SQL + N', DepartmentName = @DepartmentName';
        IF @DivisionId IS NOT NULL SET @SQL = @SQL + N', DivisionId = @DivisionId';
        IF @DivisionName IS NOT NULL SET @SQL = @SQL + N', DivisionName = @DivisionName';
        IF @ManagerId IS NOT NULL SET @SQL = @SQL + N', ManagerId = @ManagerId';
        IF @ManagerName IS NOT NULL SET @SQL = @SQL + N', ManagerName = @ManagerName';
        IF @HireDate IS NOT NULL SET @SQL = @SQL + N', HireDate = @HireDate';
        IF @TerminationDate IS NOT NULL SET @SQL = @SQL + N', TerminationDate = @TerminationDate';
        IF @EmploymentStatus IS NOT NULL SET @SQL = @SQL + N', EmploymentStatus = @EmploymentStatus';
        IF @EmployeeType IS NOT NULL SET @SQL = @SQL + N', EmployeeType = @EmployeeType';
        IF @Location IS NOT NULL SET @SQL = @SQL + N', Location = @Location';
        IF @WorkAddress IS NOT NULL SET @SQL = @SQL + N', WorkAddress = @WorkAddress';
        IF @BirthDate IS NOT NULL SET @SQL = @SQL + N', BirthDate = @BirthDate';
        IF @LinkedInProfile IS NOT NULL SET @SQL = @SQL + N', LinkedInProfile = @LinkedInProfile';
        IF @IsActive IS NOT NULL SET @SQL = @SQL + N', IsActive = @IsActive';
        IF @LastReviewDate IS NOT NULL SET @SQL = @SQL + N', LastReviewDate = @LastReviewDate';
        IF @SalaryGrade IS NOT NULL SET @SQL = @SQL + N', SalaryGrade = @SalaryGrade';
        IF @OwnerId IS NOT NULL SET @SQL = @SQL + N', OwnerId = @OwnerId';
        
        SET @SQL = @SQL + N' WHERE EmployeeId = @EmployeeId';

        -- Execute the dynamic SQL
        EXEC sp_executesql @SQL,
            N'@EmployeeId INT, @FirstName NVARCHAR(50), @LastName NVARCHAR(50), @Email NVARCHAR(100), 
              @Phone NVARCHAR(20), @MobilePhone NVARCHAR(20), @JobTitle NVARCHAR(100), 
              @DepartmentId INT, @DepartmentName NVARCHAR(100), @DivisionId INT, @DivisionName NVARCHAR(100),
              @ManagerId INT, @ManagerName NVARCHAR(100), @HireDate DATE, @TerminationDate DATE,
              @EmploymentStatus NVARCHAR(20), @EmployeeType NVARCHAR(20), @Location NVARCHAR(100),
              @WorkAddress NVARCHAR(200), @BirthDate DATE, @LinkedInProfile NVARCHAR(200),
              @IsActive BIT, @LastReviewDate DATE, @SalaryGrade NVARCHAR(10), @OwnerId INT, @Now DATETIME',
            @EmployeeId, @FirstName, @LastName, @Email, @Phone, @MobilePhone, @JobTitle,
            @DepartmentId, @DepartmentName, @DivisionId, @DivisionName, @ManagerId, @ManagerName,
            @HireDate, @TerminationDate, @EmploymentStatus, @EmployeeType, @Location, @WorkAddress,
            @BirthDate, @LinkedInProfile, @IsActive, @LastReviewDate, @SalaryGrade, @OwnerId, @Now;

        -- Return the updated employee
        SELECT * FROM Portway.CompanyEmployees WHERE EmployeeId = @EmployeeId;
    END

    -- DELETE
    ELSE IF @Method = 'DELETE'
    BEGIN
        IF @EmployeeId IS NULL
        BEGIN
            RAISERROR('EmployeeId is required for DELETE operation', 16, 1);
            RETURN;
        END

        DELETE FROM Portway.CompanyEmployees WHERE EmployeeId = @EmployeeId;
        
        -- Return success indicator
        SELECT 'Employee deleted successfully' AS Message, @EmployeeId AS EmployeeId;
    END
END
GO