IF OBJECT_ID('dbo.ServiceCancellations', 'V') IS NOT NULL
    DROP VIEW dbo.ServiceCancellations;
GO
CREATE VIEW dbo.ServiceCancellations AS
SELECT 
    sr.[RequestId],
    sr.[CustomerCode],
    sr.[Title],
    sr.[Description],
    sr.[Priority],
    sr.[Status],
    sr.[CategoryId],
    sr.[AssignedTo],
    sr.[CreatedBy],
    sr.[CreatedDate],
    sr.[LastModifiedBy],
    sr.[LastModifiedDate],
    sr.[ResolvedDate],
    sr.[ClosedDate],
    sr.[DueDate],
    sr.[IsDeleted],
    -- Additional computed columns for cancellation context
    CASE 
        WHEN sr.[Status] = 'Cancelled' AND sr.[IsDeleted] = 0 THEN 'Status Changed to Cancelled'
        WHEN sr.[IsDeleted] = 1 THEN 'Request Deleted/Cancelled'
        ELSE 'Cancelled'
    END AS CancellationType,
    -- Include category information
    c.[CategoryName],
    c.[Description] AS CategoryDescription,
    -- Get the most recent comment (useful for cancellation reasons)
    (SELECT TOP 1 [Comment] 
     FROM [dbo].[ServiceRequestComments] 
     WHERE [RequestId] = sr.[RequestId] 
     ORDER BY [CreatedDate] DESC) AS LastComment,
    -- Get cancellation date (either when status changed to cancelled or when deleted)
    COALESCE(sr.[LastModifiedDate], sr.[CreatedDate]) AS CancellationDate
FROM [dbo].[ServiceRequests] sr
LEFT JOIN [dbo].[ServiceRequestCategories] c ON sr.CategoryId = c.CategoryId
WHERE sr.[Status] = 'Cancelled' 
   OR sr.[IsDeleted] = 1;
GO

-- Grant appropriate permissions (uncomment and adjust as needed for your security model)
-- GRANT SELECT ON dbo.ServiceCancellations TO [your_api_user_role];
-- GO

-- Create an index on the underlying table to optimize the view performance
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ServiceRequests_Status_IsDeleted' AND object_id = OBJECT_ID('dbo.ServiceRequests'))
BEGIN
    CREATE INDEX IX_ServiceRequests_Status_IsDeleted 
    ON [dbo].[ServiceRequests] ([Status], [IsDeleted])
    INCLUDE ([RequestId], [CustomerCode], [CreatedDate], [LastModifiedDate]);
END
GO

-- Optional: Create a stored procedure specifically for managing cancellations
-- This can be used by the DELETE method in your ServiceCancellations endpoint
CREATE OR ALTER PROCEDURE [dbo].[sp_ManageServiceCancellations]
    @Method NVARCHAR(10), -- 'DELETE' (to cancel), 'GET' (to retrieve)
    @RequestId UNIQUEIDENTIFIER = NULL,
    @UserName NVARCHAR(50) = NULL,
    @CancellationReason NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- User validation
    IF @UserName IS NULL
    BEGIN
        SET @UserName = SUSER_SNAME();
    END
    
    IF @Method = 'DELETE' OR @Method = 'CANCEL'
    BEGIN
        -- Validate the RequestId
        IF @RequestId IS NULL
        BEGIN
            RAISERROR('RequestId is required for cancellation operations', 16, 1);
            RETURN;
        END
        
        -- Check if the request exists and isn't already cancelled
        IF NOT EXISTS(SELECT 1 FROM [dbo].[ServiceRequests] 
                     WHERE [RequestId] = @RequestId 
                     AND [Status] != 'Cancelled' 
                     AND [IsDeleted] = 0)
        BEGIN
            RAISERROR('Service request not found or already cancelled', 16, 1);
            RETURN;
        END
        
        BEGIN TRY
            BEGIN TRANSACTION;
            
            -- Update the request to cancelled status
            UPDATE [dbo].[ServiceRequests]
            SET [Status] = 'Cancelled',
                [IsDeleted] = 1,
                [LastModifiedBy] = @UserName,
                [LastModifiedDate] = GETDATE()
            WHERE [RequestId] = @RequestId;
            
            -- Add audit entry
            INSERT INTO [dbo].[ServiceRequestsAudit] (
                [RequestId], [Action], [Field], [OldValue], [NewValue], [ModifiedBy], [ModifiedDate]
            )
            VALUES (
                @RequestId, 'DELETE', 'Status', 'Active', 'Cancelled', @UserName, GETDATE()
            );
            
            -- Add cancellation comment if provided
            IF @CancellationReason IS NOT NULL AND LEN(TRIM(@CancellationReason)) > 0
            BEGIN
                INSERT INTO [dbo].[ServiceRequestComments] (
                    [RequestId], [Comment], [IsInternal], [CreatedBy], [CreatedDate]
                )
                VALUES (
                    @RequestId, 
                    CONCAT('Request cancelled. Reason: ', @CancellationReason), 
                    1, 
                    @UserName, 
                    GETDATE()
                );
            END
            
            COMMIT TRANSACTION;
            
            -- Return the cancelled request details
            SELECT * FROM dbo.ServiceCancellations WHERE RequestId = @RequestId;
            
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0
                ROLLBACK TRANSACTION;
                
            DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
            RAISERROR(@ErrorMessage, 16, 1);
        END CATCH
    END
    ELSE IF @Method = 'GET'
    BEGIN
        -- Return cancellation details
        IF @RequestId IS NOT NULL
        BEGIN
            SELECT * FROM dbo.ServiceCancellations WHERE RequestId = @RequestId;
        END
        ELSE
        BEGIN
            SELECT * FROM dbo.ServiceCancellations ORDER BY CancellationDate DESC;
        END
    END
    ELSE
    BEGIN
        RAISERROR('Invalid method. Supported methods: GET, DELETE', 16, 1);
    END
END;
GO
