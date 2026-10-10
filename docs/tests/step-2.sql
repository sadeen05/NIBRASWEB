-- Step 2 acceptance checks. Expected results are documented before each statement.
SET QUOTED_IDENTIFIER ON;
GO

-- EXPECTED: exactly 2 rows; TitleDeed and EncumbranceStatement both return 1.
SELECT NAME, IS_REQUIRED_FOR_VERIFICATION
FROM DOCUMENT_TYPES
WHERE NAME IN ('TitleDeed', 'EncumbranceStatement')
ORDER BY ID;

-- EXPECTED: exactly 1 row; MIN_ELEVATION_M is NULL, REASON starts with ILLUSTRATIVE,
-- and UPDATED_AT is not NULL.
SELECT ID, MIN_ELEVATION_M, REASON, UPDATED_AT
FROM LAND_CRITERIA;

-- EXPECTED: exactly 4 rows.
SELECT COUNT(*) AS ROLE_COUNT
FROM ROLES;

-- EXPECTED: exactly 1 row for the system user with ID -1.
SELECT ID, ROLE_ID, EMAIL
FROM USERS
WHERE ID = -1;

-- EXPECTED: one row; IS_UNIQUE = 1 and FILTER_DEFINITION indicates CONTRACT_ID IS NOT NULL.
SELECT i.name, i.is_unique, i.filter_definition
FROM sys.indexes AS i
WHERE i.object_id = OBJECT_ID('GRID_CAPACITY_RESERVATIONS')
  AND i.name = 'UQ_GRID_CAPACITY_RESERVATIONS_CONTRACT_ID';

-- EXPECTED: one row; IS_UNIQUE = 1 and FILTER_DEFINITION indicates IS_DELETED = 0.
SELECT i.name, i.is_unique, i.filter_definition
FROM sys.indexes AS i
WHERE i.object_id = OBJECT_ID('LANDS')
  AND i.name = 'UQ_LAND_LOCATION';

-- EXPECTED: the first land insert succeeds; the second insert fails with a
-- duplicate-key error; the third insert with a different ParcelNumber succeeds.
BEGIN TRANSACTION;

DECLARE @RegionId int;
DECLARE @GridId int;
DECLARE @LandlordId int;

INSERT INTO REGIONS (NAME_AR, NAME_EN, PEAK_SUN_HOURS_PER_DAY, WHEELING_FEE_PER_KWH, LOSS_PERCENTAGE)
VALUES (N'قبول', N'Acceptance Region', 5.5, 0, 0);
SET @RegionId = CONVERT(int, SCOPE_IDENTITY());

INSERT INTO GRIDS (REGION_ID, NAME, CAPACITY_MW, STATUS)
VALUES (@RegionId, N'Acceptance Grid', 100, N'Active');
SET @GridId = CONVERT(int, SCOPE_IDENTITY());

INSERT INTO USERS (ROLE_ID, FULL_NAME, EMAIL, PHONE, PASSWORD_HASH, IS_DELETED, IS_APPROVED)
VALUES (1, N'Acceptance Landlord', N'acceptance-landlord@nibras.test', N'0500000001', N'acceptance-password-hash', 0, 0);
SET @LandlordId = CONVERT(int, SCOPE_IDENTITY());

INSERT INTO LANDS
    (LANDLORD_ID, REGION_ID, GRID_ID, LAND_NUMBER, VILLAGE, BASIN, PARCEL_NUMBER,
     AREA_DONUM, SLOPE_PERCENTAGE, DISTANCE_TO_GRID_KM, SOLAR_IRRADIANCE,
     ELEVATION_M, IS_DELETED, LAND_STATUS_ID, DATA_VERIFIED_BY_ADMIN)
VALUES
    (@LandlordId, @RegionId, @GridId, N'ACC-001', N'Village A', N'Basin A', N'Parcel 1',
     20, 5, 10, 5, 100, 0, 1, 0);

-- EXPECTED: this statement fails with a duplicate-key violation for UQ_LAND_LOCATION.
BEGIN TRY
    INSERT INTO LANDS
        (LANDLORD_ID, REGION_ID, GRID_ID, LAND_NUMBER, VILLAGE, BASIN, PARCEL_NUMBER,
         AREA_DONUM, SLOPE_PERCENTAGE, DISTANCE_TO_GRID_KM, SOLAR_IRRADIANCE,
         ELEVATION_M, IS_DELETED, LAND_STATUS_ID, DATA_VERIFIED_BY_ADMIN)
    VALUES
        (@LandlordId, @RegionId, @GridId, N'ACC-002', N'Village A', N'Basin A', N'Parcel 1',
         20, 5, 10, 5, 100, 0, 1, 0);
    PRINT 'UNEXPECTED: duplicate land insert succeeded';
END TRY
BEGIN CATCH
    PRINT 'EXPECTED: duplicate land insert failed: ' + ERROR_MESSAGE();
END CATCH;

-- EXPECTED: this statement succeeds because PARCEL_NUMBER differs.
INSERT INTO LANDS
    (LANDLORD_ID, REGION_ID, GRID_ID, LAND_NUMBER, VILLAGE, BASIN, PARCEL_NUMBER,
     AREA_DONUM, SLOPE_PERCENTAGE, DISTANCE_TO_GRID_KM, SOLAR_IRRADIANCE,
     ELEVATION_M, IS_DELETED, LAND_STATUS_ID, DATA_VERIFIED_BY_ADMIN)
VALUES
    (@LandlordId, @RegionId, @GridId, N'ACC-003', N'Village A', N'Basin A', N'Parcel 2',
     20, 5, 10, 5, 100, 0, 1, 0);

-- EXPECTED: 2 rows are visible inside the transaction before rollback.
SELECT COUNT(*) AS ACCEPTANCE_LAND_ROWS
FROM LANDS
WHERE REGION_ID = @RegionId
  AND VILLAGE = N'Village A'
  AND BASIN = N'Basin A';

ROLLBACK TRANSACTION;

-- EXPECTED: 0 rows; the acceptance transaction rolled back all created data.
SELECT COUNT(*) AS ROLLED_BACK_LAND_ROWS
FROM LANDS
WHERE LAND_NUMBER IN (N'ACC-001', N'ACC-003');

-- EXPECTED: FILE_PATH max length 500 and REVIEW_NOTE max length 500.
SELECT c.name, c.max_length / 2 AS MAX_LENGTH
FROM sys.columns AS c
WHERE c.object_id = OBJECT_ID('LAND_DOCUMENTS')
  AND c.name IN ('FILE_PATH', 'REVIEW_NOTE')
ORDER BY c.name;

-- EXPECTED: PASSWORD_HASH max length is at least 100.
SELECT c.name, c.max_length / 2 AS MAX_LENGTH
FROM sys.columns AS c
WHERE c.object_id = OBJECT_ID('USERS')
  AND c.name = 'PASSWORD_HASH';
