USE [600]
GO
/****** Object:  StoredProcedure [dbo].[sp_ManageServiceRequests]    Script Date: 28-10-2025 09:53:03 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
ALTER   PROCEDURE [dbo].[sp_ManageServiceRequests]
    @Method NVARCHAR(10), -- 'INSERT', 'UPDATE', or 'DELETE'
    -- Primary key for existing records
    @Id UNIQUEIDENTIFIER = NULL,
    -- Fields for INSERT/UPDATE
    @CustomerCode NVARCHAR(20) = NULL,
    @Title NVARCHAR(100) = NULL,
    @Description NVARCHAR(MAX) = NULL,
    @Priority INT = NULL,
    @Status NVARCHAR(20) = NULL,
    @CategoryId INT = NULL,
    @AssignedTo NVARCHAR(50) = NULL,
    @DueDate DATETIME = NULL,
    -- Audit fields
    @UserName NVARCHAR(50) = NULL, -- Current user performing the action
    -- Optional comment to add
    @Comment NVARCHAR(MAX) = NULL,
    @CommentIsInternal BIT = 0,
	@CreatedBy NVARCHAR(50) = NULL,
	@CreatedDate DATETIME = NULL,
	@ModifiedBy NVARCHAR(50) = NULL,
	@ModifiedDate DATETIME = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Get the current time for audit and update timestamps
    DECLARE @CurrentTime DATETIME = GETDATE();
    
    -- User validation
    IF @UserName IS NULL
    BEGIN
        SET @UserName = SUSER_SNAME(); -- Default to current SQL user if not specified
    END
    
    -- Error handling variables
    DECLARE @ErrorMessage NVARCHAR(4000);
    DECLARE @ErrorSeverity INT;
    DECLARE @ErrorState INT;
    
    BEGIN TRY
        -- BEGIN TRANSACTION to ensure all operations are atomic
        BEGIN TRANSACTION;
        
        -- INSERT operation
        IF @Method = 'INSERT'
        BEGIN
            -- Validate required fields
            IF @CustomerCode IS NULL OR @Title IS NULL
            BEGIN
                RAISERROR('CustomerCode and Title are required for INSERT operations', 16, 1);
                RETURN;
            END
            
            -- Generate a new ID if not provided
            IF @Id IS NULL
            BEGIN
                SET @Id = NEWID();
            END
            
			IF EXISTS (SELECT 1 FROM [dbo].[ServiceRequests] WHERE [RequestId] = @Id)
			BEGIN
                RAISERROR('Record already exists.', 16, 1);
                RETURN;
			END
            -- Set default values if not provided
            IF @Priority IS NULL SET @Priority = 3; -- Default to Low priority
            IF @Status IS NULL SET @Status = 'New'; -- Default status
            
            -- Insert the new service request
            INSERT INTO [dbo].[ServiceRequests] (
                [RequestId], [CustomerCode], [Title], [Description], 
                [Priority], [Status], [CategoryId], [AssignedTo],
                [CreatedBy], [CreatedDate], [LastModifiedBy], [LastModifiedDate],
                [DueDate], [IsDeleted]
            )
            VALUES (
                @Id, @CustomerCode, @Title, @Description,
                @Priority, @Status, @CategoryId, @AssignedTo,
                @UserName, @CurrentTime, @UserName, @CurrentTime,
                @DueDate, 0
            );
            
            -- Add initial audit entry
            INSERT INTO [dbo].[ServiceRequestsAudit] (
                [RequestId], [Action], [Field], [OldValue], [NewValue], [ModifiedBy], [ModifiedDate]
            )
            VALUES (
                @Id, 'INSERT', 'ALL', NULL, 'New service request created', @UserName, @CurrentTime
            );
            
            -- Add initial comment if provided
            IF @Comment IS NOT NULL AND LEN(TRIM(@Comment)) > 0
            BEGIN
                INSERT INTO [dbo].[ServiceRequestComments] (
                    [RequestId], [Comment], [IsInternal], [CreatedBy], [CreatedDate]
                )
                VALUES (
                    @Id, @Comment, @CommentIsInternal, @UserName, @CurrentTime
                );
            END
            
            -- Return the newly created record
            SELECT 
                sr.*,
                c.CategoryName,
                ISNULL((SELECT TOP 1 [CreatedDate] FROM [dbo].[ServiceRequestComments] 
                        WHERE [RequestId] = sr.[RequestId] 
                        ORDER BY [CreatedDate] DESC), NULL) AS LastCommentDate
            FROM [dbo].[ServiceRequests] sr
            LEFT JOIN [dbo].[ServiceRequestCategories] c ON sr.CategoryId = c.CategoryId
            WHERE sr.[RequestId] = @Id;
        END
        
        -- UPDATE operation
        ELSE IF @Method = 'UPDATE'
        BEGIN
            -- Validate the primary key
            IF @Id IS NULL
            BEGIN
                RAISERROR('RequestId is required for UPDATE operations', 16, 1);
                RETURN;
            END
            
            -- Check if the record exists and isn't deleted
            IF NOT EXISTS(SELECT 1 FROM [dbo].[ServiceRequests] WHERE [RequestId] = @Id AND [IsDeleted] = 0)
            BEGIN
                RAISERROR('Service request with specified ID not found or has been deleted', 16, 1);
                RETURN;
            END
            
            -- Store original values for audit
            DECLARE @OldCustomerCode NVARCHAR(20),
                    @OldTitle NVARCHAR(100),
                    @OldDescription NVARCHAR(MAX),
                    @OldPriority INT,
                    @OldStatus NVARCHAR(20),
                    @OldCategoryId INT,
                    @OldAssignedTo NVARCHAR(50),
                    @OldDueDate DATETIME;
                    
            SELECT @OldCustomerCode = [CustomerCode],
                   @OldTitle = [Title],
                   @OldDescription = [Description],
                   @OldPriority = [Priority],
                   @OldStatus = [Status],
                   @OldCategoryId = [CategoryId],
                   @OldAssignedTo = [AssignedTo],
                   @OldDueDate = [DueDate]
            FROM [dbo].[ServiceRequests]
            WHERE [RequestId] = @Id;
            
            -- Handle status-specific date fields
            IF @Status IS NOT NULL AND @Status <> @OldStatus
            BEGIN
                -- If status changed to Resolved, set ResolvedDate
                IF @Status = 'Resolved' AND @OldStatus <> 'Resolved'
                BEGIN
                    UPDATE [dbo].[ServiceRequests]
                    SET [ResolvedDate] = @CurrentTime
                    WHERE [RequestId] = @Id;
                    
                    -- Add audit entry for status change to Resolved
                    INSERT INTO [dbo].[ServiceRequestsAudit] (
                        [RequestId], [Action], [Field], [OldValue], [NewValue], [ModifiedBy], [ModifiedDate]
                    )
                    VALUES (
                        @Id, 'UPDATE', 'ResolvedDate', NULL, CONVERT(NVARCHAR(50), @CurrentTime, 121), @UserName, @CurrentTime
                    );
                END
                
                -- If status changed to Closed, set ClosedDate
                IF @Status = 'Closed' AND @OldStatus <> 'Closed'
                BEGIN
                    UPDATE [dbo].[ServiceRequests]
                    SET [ClosedDate] = @CurrentTime
                    WHERE [RequestId] = @Id;
                    
                    -- Add audit entry for status change to Closed
                    INSERT INTO [dbo].[ServiceRequestsAudit] (
                        [RequestId], [Action], [Field], [OldValue], [NewValue], [ModifiedBy], [ModifiedDate]
                    )
                    VALUES (
                        @Id, 'UPDATE', 'ClosedDate', NULL, CONVERT(NVARCHAR(50), @CurrentTime, 121), @UserName, @CurrentTime
                    );
                END
            END
            
            -- Update the service request with all provided fields
            UPDATE [dbo].[ServiceRequests]
            SET [CustomerCode] = ISNULL(@CustomerCode, [CustomerCode]),
                [Title] = ISNULL(@Title, [Title]),
                [Description] = ISNULL(@Description, [Description]),
                [Priority] = ISNULL(@Priority, [Priority]),
                [Status] = ISNULL(@Status, [Status]),
                [CategoryId] = ISNULL(@CategoryId, [CategoryId]),
                [AssignedTo] = ISNULL(@AssignedTo, [AssignedTo]),
                [LastModifiedBy] = @UserName,
                [LastModifiedDate] = @CurrentTime,
                [DueDate] = ISNULL(@DueDate, [DueDate])
            WHERE [RequestId] = @Id;
            
            -- Add audit entries for each changed field
            IF @CustomerCode IS NOT NULL AND @CustomerCode <> @OldCustomerCode
                INSERT INTO [dbo].[ServiceRequestsAudit] VALUES(@Id, 'UPDATE', 'CustomerCode', @OldCustomerCode, @CustomerCode, @UserName, @CurrentTime);
            
            IF @Title IS NOT NULL AND @Title <> @OldTitle
                INSERT INTO [dbo].[ServiceRequestsAudit] VALUES(@Id, 'UPDATE', 'Title', @OldTitle, @Title, @UserName, @CurrentTime);
            
            IF @Description IS NOT NULL AND @Description <> @OldDescription
                INSERT INTO [dbo].[ServiceRequestsAudit] VALUES(@Id, 'UPDATE', 'Description', @OldDescription, @Description, @UserName, @CurrentTime);
            
            IF @Priority IS NOT NULL AND @Priority <> @OldPriority
                INSERT INTO [dbo].[ServiceRequestsAudit] VALUES(@Id, 'UPDATE', 'Priority', CAST(@OldPriority AS NVARCHAR(10)), CAST(@Priority AS NVARCHAR(10)), @UserName, @CurrentTime);
            
            IF @Status IS NOT NULL AND @Status <> @OldStatus
                INSERT INTO [dbo].[ServiceRequestsAudit] VALUES(@Id, 'UPDATE', 'Status', @OldStatus, @Status, @UserName, @CurrentTime);
            
            IF @CategoryId IS NOT NULL AND @CategoryId <> @OldCategoryId
                INSERT INTO [dbo].[ServiceRequestsAudit] VALUES(@Id, 'UPDATE', 'CategoryId', CAST(@OldCategoryId AS NVARCHAR(10)), CAST(@CategoryId AS NVARCHAR(10)), @UserName, @CurrentTime);
            
            IF @AssignedTo IS NOT NULL AND @AssignedTo <> @OldAssignedTo
                INSERT INTO [dbo].[ServiceRequestsAudit] VALUES(@Id, 'UPDATE', 'AssignedTo', @OldAssignedTo, @AssignedTo, @UserName, @CurrentTime);
            
            IF @DueDate IS NOT NULL AND @DueDate <> @OldDueDate
                INSERT INTO [dbo].[ServiceRequestsAudit] VALUES(@Id, 'UPDATE', 'DueDate', CONVERT(NVARCHAR(50), @OldDueDate, 121), CONVERT(NVARCHAR(50), @DueDate, 121), @UserName, @CurrentTime);
            
            -- Add comment if provided
            IF @Comment IS NOT NULL AND LEN(TRIM(@Comment)) > 0
            BEGIN
                INSERT INTO [dbo].[ServiceRequestComments] (
                    [RequestId], [Comment], [IsInternal], [CreatedBy], [CreatedDate]
                )
                VALUES (
                    @Id, @Comment, @CommentIsInternal, @UserName, @CurrentTime
                );
            END
            
            -- Return the updated record with category name
            SELECT 
                sr.*,
                c.CategoryName,
                ISNULL((SELECT TOP 1 [CreatedDate] FROM [dbo].[ServiceRequestComments] 
                        WHERE [RequestId] = sr.[RequestId] 
                        ORDER BY [CreatedDate] DESC), NULL) AS LastCommentDate
            FROM [dbo].[ServiceRequests] sr
            LEFT JOIN [dbo].[ServiceRequestCategories] c ON sr.CategoryId = c.CategoryId
            WHERE sr.[RequestId] = @Id;
        END
        
        -- DELETE operation (soft delete)
        ELSE IF @Method = 'DELETE'
        BEGIN
            -- Validate the primary key
            IF @Id IS NULL
            BEGIN
                RAISERROR('RequestId is required for DELETE operations', 16, 1);
                RETURN;
            END
            
            -- Check if the record exists and isn't already deleted
            IF NOT EXISTS(SELECT 1 FROM [dbo].[ServiceRequests] WHERE [RequestId] = @Id AND [IsDeleted] = 0)
            BEGIN
                RAISERROR('Service request with specified ID not found or has already been deleted', 16, 1);
                RETURN;
            END
            
            -- Perform soft delete
            UPDATE [dbo].[ServiceRequests]
            SET [IsDeleted] = 1,
                [Status] = 'Cancelled',
                [LastModifiedBy] = @UserName,
                [LastModifiedDate] = @CurrentTime
            WHERE [RequestId] = @Id;
            
            -- Add audit entry for deletion
            INSERT INTO [dbo].[ServiceRequestsAudit] (
                [RequestId], [Action], [Field], [OldValue], [NewValue], [ModifiedBy], [ModifiedDate]
            )
            VALUES (
                @Id, 'DELETE', 'IsDeleted', '0', '1', @UserName, @CurrentTime
            );
            
            -- Add comment for deletion if provided
            IF @Comment IS NOT NULL AND LEN(TRIM(@Comment)) > 0
            BEGIN
                INSERT INTO [dbo].[ServiceRequestComments] (
                    [RequestId], [Comment], [IsInternal], [CreatedBy], [CreatedDate]
                )
                VALUES (
                    @Id, CONCAT('Request deleted. Reason: ', @Comment), 1, @UserName, @CurrentTime
                );
            END
            
            -- Return success message
            SELECT 'Service request deleted successfully' AS Message, @Id AS DeletedId;
        END
        ELSE
        BEGIN
            -- Invalid method
            RAISERROR('Invalid method. Supported methods: INSERT, UPDATE, DELETE', 16, 1);
            RETURN;
        END
        
        -- COMMIT the transaction if everything succeeded
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- ROLLBACK the transaction on error
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        
        SELECT 
            @ErrorMessage = ERROR_MESSAGE(),
            @ErrorSeverity = ERROR_SEVERITY(),
            @ErrorState = ERROR_STATE();
            
        -- Return error information
        SELECT 
            ERROR_NUMBER() AS ErrorNumber,
            @ErrorMessage AS ErrorMessage,
            ERROR_LINE() AS ErrorLine,
            ERROR_PROCEDURE() AS ErrorProcedure;
            
        -- Re-throw the error with the original severity and state
        RAISERROR (@ErrorMessage, @ErrorSeverity, @ErrorState);
    END CATCH
END;
GO