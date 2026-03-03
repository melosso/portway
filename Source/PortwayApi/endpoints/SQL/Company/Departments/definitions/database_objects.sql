CREATE OR ALTER FUNCTION dbo.GenerateSampleUsers(
    @DepartmentId INT = NULL,
    @BaseDate DATE = '2025-01-01'
)
RETURNS TABLE
AS
RETURN (
    WITH Numbers AS (
        -- Generate 1..1000 rows using system table
        SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS RowNum
        FROM master..spt_values
    ),
    Departments AS (
        SELECT v.DeptId,
               v.DeptName
        FROM (VALUES
            (1, 'Engineering'),
            (2, 'Marketing'),
            (3, 'Sales'),
            (4, 'Human Resources'),
            (5, 'Finance'),
            (6, 'Operations'),
            (7, 'Customer Service'),
            (8, 'IT Support'),
            (9, 'Research & Development'),
            (10, 'Administration')
        ) v(DeptId, DeptName)
    ),
    UserData AS (
        SELECT 
            n.RowNum,
            
            -- If DepartmentId is NULL → assign a pseudo-random department
            ISNULL(@DepartmentId, ((n.RowNum % 10) + 1)) AS department_id,

            (ISNULL(@DepartmentId, ((n.RowNum % 10) + 1)) * 1000) + n.RowNum AS user_id,

            -- Cycle through sample first names
            CHOOSE((n.RowNum % 20) + 1,
                'James','Mary','John','Patricia','Robert','Jennifer','Michael','Linda',
                'William','Elizabeth','David','Barbara','Richard','Susan','Joseph','Jessica',
                'Thomas','Sarah','Christopher','Karen'
            ) AS first_name,

            -- Cycle through sample last names
            CHOOSE((n.RowNum % 20) + 1,
                'Smith','Johnson','Williams','Brown','Jones','Garcia','Miller','Davis',
                'Rodriguez','Martinez','Hernandez','Lopez','Gonzalez','Wilson','Anderson','Thomas',
                'Taylor','Moore','Jackson','Martin'
            ) AS last_name,

            DATEADD(DAY, -(n.RowNum % 1825), @BaseDate) AS hire_date,

            -- Simple salary ranges by department
            CASE 
                WHEN ISNULL(@DepartmentId, ((n.RowNum % 10) + 1)) IN (1, 9) 
                    THEN 75000 + (n.RowNum % 50000)
                WHEN ISNULL(@DepartmentId, ((n.RowNum % 10) + 1)) IN (2, 3) 
                    THEN 50000 + (n.RowNum % 40000)
                WHEN ISNULL(@DepartmentId, ((n.RowNum % 10) + 1)) = 5 
                    THEN 60000 + (n.RowNum % 45000)
                ELSE 45000 + (n.RowNum % 35000)
            END AS salary,

            -- Active flag (90% active by pattern)
            CASE WHEN (n.RowNum % 10) < 9 THEN 1 ELSE 0 END AS is_active
        FROM Numbers n
    )
    SELECT 
        u.user_id,
        u.first_name,
        u.last_name,
        LOWER(u.first_name + '.' + u.last_name + '@company.com') AS email,
        u.department_id,
        d.DeptName AS department_name,
        u.hire_date,
        u.salary,
        u.is_active
    FROM UserData u
    INNER JOIN Departments d ON u.department_id = d.DeptId
);
GO

/*
Example usage:

-- 1. Generate users with random departments
SELECT TOP 20 * FROM dbo.GenerateSampleUsers(DEFAULT, DEFAULT);

-- 2. Generate users for Sales department (DeptId = 3)
SELECT TOP 20 * FROM dbo.GenerateSampleUsers(3, DEFAULT);

-- 3. Generate users for Finance department (DeptId = 5)
SELECT TOP 20 * FROM dbo.GenerateSampleUsers(5, DEFAULT);

-- 4. Generate users with a different base hire date
SELECT TOP 20 * FROM dbo.GenerateSampleUsers(2, '2015-01-01');
*/
