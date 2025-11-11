# Portway API Tests

This directory contains unit and integration tests for the Portway API. The tests focus on validating that the core GET functionality of the API works correctly.

## Test Structure

The tests are organized into the following categories:

- **Base** - Base test classes and utilities for testing
- **Endpoints** - Tests for API endpoints (SQL, Proxy, Health)
- **Services** - Tests for internal services (Environment Settings, OData to SQL conversion)

## Running the Tests

### Using Visual Studio

1. Open the Portway solution in Visual Studio
2. Right-click on the PortwayApi.Tests project in Solution Explorer
3. Select "Run Tests"

### Using Command Line

1. Navigate to the Tests directory
2. Run `dotnet test` to execute all tests
3. Alternatively, run `RunTests.bat` for a preconfigured test execution

For running specific tests:
```
dotnet test --filter "FullyQualifiedName~SqlEndpointTests"
```

## Test Coverage

These tests cover:

1. **SQL Endpoints**
   - Basic GET request validation
   - ID-based GET requests
   - Environment validation
   - Authentication requirements

2. **Proxy Endpoints**
   - GET request with various parameters
   - URL construction and forwarding
   - ID and query parameter handling

3. **Environment Settings**
   - Environment validation
   - Configuration loading

4. **OData to SQL Conversion**
   - Validation of SQL query generation from OData parameters
   - Filter, select, orderby, top, and skip parameters

5. **Health Checks**
   - Validation of health check endpoints
   - Authentication requirements

## Troubleshooting

### Common Issues

1. **Test failures due to port conflicts**
   - The tests may fail if port 5000 or 5001 is in use
   - Close any running instances of the Portway API

2. **Database access errors**
   - The tests use mocks for database access
   - No actual database is required to run the tests

3. **Missing dependencies**
   - Ensure all NuGet packages are restored before running tests
   - Run `dotnet restore` if needed

## Adding More Tests

To add more tests:

1. Follow the existing patterns in the respective test files
2. Use the ApiTestBase class for testing endpoints
3. Mock any external dependencies
4. Use descriptive test names following the pattern `Method_Scenario_ExpectedResult`