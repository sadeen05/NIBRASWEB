-- =============================================================================
-- FORENSIC READ-ONLY INVENTORY QUERIES
-- =============================================================================
-- Target: SADEEN\SQLEXPRESS / NIBRAS_F1_Verify
-- Authorization: REQUIRED - separate written approval before execution
-- Execution: Read-only SELECT only. No INSERT/UPDATE/DELETE/MERGE/DDL.
-- Purpose: Exact enumeration of contamination footprint for remediation planning.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 0. IDENTITY VERIFICATION (MUST RUN FIRST)
-- -----------------------------------------------------------------------------
SELECT
    @@SERVERNAME AS ServerName,
    DB_NAME() AS DatabaseName,
    SERVERPROPERTY('ServerName') AS InstanceName,
    SERVERPROPERTY('InstanceName') AS NamedInstance
;

-- Expected: ServerName = 'SADEEN\SQLEXPRESS', DatabaseName = 'NIBRAS_F1_Verify'
-- ABORT if any other identity is returned.

-- -----------------------------------------------------------------------------
-- 1. CRITERIA CONTAMINATION INVENTORY (IDs 42-56)
-- -----------------------------------------------------------------------------
SELECT
    ID,
    MIN_AREA_DONUM,
    MAX_SLOPE_PCT,
    MAX_GRID_DISTANCE_KM,
    MIN_SOLAR_IRRADIANCE,
    MIN_ELEVATION_M,
    PREVIOUS_CRITERION_ID,
    UPDATED_BY_ID,
    UPDATED_AT,
    REASON,
    CASE
        WHEN MIN_AREA_DONUM IS NULL OR MAX_SLOPE_PCT IS NULL OR MAX_GRID_DISTANCE_KM IS NULL
        THEN 'FIXTURE (null thresholds)'
        WHEN REASON LIKE '%fixture%' THEN 'FIXTURE (reason)'
        ELSE 'POLICY'
    END AS Classification
FROM LAND_CRITERIA
WHERE ID BETWEEN 42 AND 56
ORDER BY ID;

-- Expected: 42-45, 46-49, 51-54 = FIXTURE; 50, 55, 56 = POLICY
-- Note: 55 has PREVIOUS_CRITERION_ID = 54 (fixture), 56 has PREVIOUS_CRITERION_ID = 55

-- -----------------------------------------------------------------------------
-- 2. UNEXPECTED DOCUMENT ROWS (155 rows, 31 lands)
-- -----------------------------------------------------------------------------
-- 2a. Full enumeration with land and type context
SELECT
    d.ID AS DocumentID,
    d.LAND_ID,
    l.LAND_NUMBER,
    d.DOCUMENT_TYPE_ID,
    dt.NAME AS DocumentTypeName,
    dt.IS_REQUIRED_FOR_VERIFICATION,
    d.VERSION,
    d.STATUS,
    d.REVIEWED_BY_ID,
    u.FullName AS ReviewerName,
    d.REVIEWED_AT,
    d.UPLOADED_AT,
    d.FILE_PATH
FROM LAND_DOCUMENTS d
JOIN LANDS l ON l.ID = d.LAND_ID
JOIN DOCUMENT_TYPES dt ON dt.ID = d.DOCUMENT_TYPE_ID
LEFT JOIN USERS u ON u.ID = d.REVIEWED_BY_ID
WHERE d.FILE_PATH LIKE 'fixture/f1race-completion-%'
ORDER BY d.LAND_ID, d.DOCUMENT_TYPE_ID, d.VERSION;

-- 2b. Per-land, per-type summary (verifies completion coverage)
SELECT
    d.LAND_ID,
    l.LAND_NUMBER,
    d.DOCUMENT_TYPE_ID,
    dt.NAME AS DocumentTypeName,
    dt.IS_REQUIRED_FOR_VERIFICATION,
    COUNT(*) AS RowCount,
    MIN(d.VERSION) AS MinVersion,
    MAX(d.VERSION) AS MaxVersion,
    MIN(d.FILE_PATH) AS FirstFilePath,
    MAX(d.FILE_PATH) AS LastFilePath
FROM LAND_DOCUMENTS d
JOIN LANDS l ON l.ID = d.LAND_ID
JOIN DOCUMENT_TYPES dt ON dt.ID = d.DOCUMENT_TYPE_ID
WHERE d.FILE_PATH LIKE 'fixture/f1race-completion-%'
GROUP BY d.LAND_ID, l.LAND_NUMBER, d.DOCUMENT_TYPE_ID, dt.NAME, dt.IS_REQUIRED_FOR_VERIFICATION
ORDER BY d.LAND_ID, d.DOCUMENT_TYPE_ID;

-- 2c. Distinct affected land IDs (should be 31)
SELECT DISTINCT
    d.LAND_ID,
    l.LAND_NUMBER,
    l.STATUS AS LandStatus,
    l.LANDLORD_ID,
    l.VERIFIED_AGAINST_CRITERION_ID
FROM LAND_DOCUMENTS d
JOIN LANDS l ON l.ID = d.LAND_ID
WHERE d.FILE_PATH LIKE 'fixture/f1race-completion-%'
ORDER BY d.LAND_ID;

-- -----------------------------------------------------------------------------
-- 3. EXPECTED FIXTURE DOCUMENTS (current f1req-* lands)
-- -----------------------------------------------------------------------------
SELECT
    d.ID,
    d.LAND_ID,
    l.LAND_NUMBER,
    d.DOCUMENT_TYPE_ID,
    dt.NAME AS DocumentTypeName,
    dt.IS_REQUIRED_FOR_VERIFICATION,
    d.VERSION,
    d.STATUS,
    d.FILE_PATH
FROM LAND_DOCUMENTS d
JOIN LANDS l ON l.ID = d.LAND_ID
JOIN DOCUMENT_TYPES dt ON dt.ID = d.DOCUMENT_TYPE_ID
WHERE l.LAND_NUMBER LIKE 'f1req-%'
ORDER BY d.LAND_ID, d.DOCUMENT_TYPE_ID, d.VERSION;

-- -----------------------------------------------------------------------------
-- 4. GLOBAL DOCUMENT-TYPE REQUIRED FLAGS
-- -----------------------------------------------------------------------------
SELECT
    ID,
    NAME,
    IS_REQUIRED_FOR_VERIFICATION
FROM DOCUMENT_TYPES
WHERE IS_REQUIRED_FOR_VERIFICATION = 1
ORDER BY ID;

-- Expected: Only the true globally required types (e.g., TitleDeed=1, EncumbranceStatement=2)
-- No f1req-* or f1race-* types should be required.

-- -----------------------------------------------------------------------------
-- 5. AUDIT REFERENCES AND DOCUMENT LINEAGE
-- -----------------------------------------------------------------------------
-- 5a. Land status history for affected lands
SELECT
    h.ID,
    h.LAND_ID,
    h.STATUS,
    h.CHANGED_BY_ID,
    h.REASON,
    h.CHANGED_AT
FROM LAND_STATUS_HISTORIES h
WHERE h.LAND_ID IN (
    SELECT DISTINCT LAND_ID FROM LAND_DOCUMENTS WHERE FILE_PATH LIKE 'fixture/f1race-completion-%'
)
ORDER BY h.LAND_ID, h.ID;

-- 5b. User security events for affected document reviewers
SELECT
    e.ID,
    e.USER_ID,
    e.EVENT_TYPE,
    e.CREATED_AT,
    e.DETAIL
FROM USER_SECURITY_EVENTS e
WHERE e.USER_ID IN (
    SELECT DISTINCT REVIEWED_BY_ID FROM LAND_DOCUMENTS WHERE FILE_PATH LIKE 'fixture/f1race-completion-%'
)
ORDER BY e.USER_ID, e.ID;

-- 5c. Document audit trail (if exists)
SELECT
    da.ID,
    da.LAND_DOCUMENT_ID,
    da.ACTION,
    da.CHANGED_BY_ID,
    da.CHANGED_AT,
    da.DETAIL
FROM DOCUMENT_AUDIT da
WHERE da.LAND_DOCUMENT_ID IN (
    SELECT ID FROM LAND_DOCUMENTS WHERE FILE_PATH LIKE 'fixture/f1race-completion-%'
)
ORDER BY da.LAND_DOCUMENT_ID, da.ID;

-- -----------------------------------------------------------------------------
-- 6. FIXTURE USER INVENTORY (f1host-* users created today)
-- -----------------------------------------------------------------------------
SELECT
    ID,
    EMAIL,
    ROLE_ID,
    EMAIL_VERIFIED,
    IS_APPROVED,
    IS_DELETED,
    CREATED_AT
FROM USERS
WHERE EMAIL LIKE 'f1host-%@example.test'
ORDER BY ID DESC;

-- -----------------------------------------------------------------------------
-- 7. FIXTURE DOCUMENT TYPE INVENTORY
-- -----------------------------------------------------------------------------
SELECT
    ID,
    NAME,
    IS_REQUIRED_FOR_VERIFICATION,
    UPDATED_AT,
    UPDATED_BY_ID
FROM DOCUMENT_TYPES
WHERE NAME LIKE 'f1req-type-%' OR NAME LIKE 'f1race-type-%'
ORDER BY NAME;

-- -----------------------------------------------------------------------------
-- 8. CRITERIA LINEAGE VERIFICATION
-- -----------------------------------------------------------------------------
WITH CriteriaChain AS (
    SELECT
        c.ID,
        c.PREVIOUS_CRITERION_ID,
        c.MIN_AREA_DONUM,
        c.MAX_SLOPE_PCT,
        c.MAX_GRID_DISTANCE_KM,
        c.REASON,
        0 AS Depth
    FROM LAND_CRITERIA c
    WHERE c.ID = (SELECT MAX(ID) FROM LAND_CRITERIA)
    
    UNION ALL
    
    SELECT
        c.ID,
        c.PREVIOUS_CRITERION_ID,
        c.MIN_AREA_DONUM,
        c.MAX_SLOPE_PCT,
        c.MAX_GRID_DISTANCE_KM,
        c.REASON,
        cc.Depth + 1
    FROM LAND_CRITERIA c
    JOIN CriteriaChain cc ON c.ID = cc.PREVIOUS_CRITERION_ID
    WHERE cc.Depth < 20
)
SELECT
    ID,
    PREVIOUS_CRITERION_ID,
    MIN_AREA_DONUM,
    MAX_SLOPE_PCT,
    MAX_GRID_DISTANCE_KM,
    REASON,
    CASE
        WHEN MIN_AREA_DONUM IS NULL THEN 'FIXTURE (null)'
        WHEN REASON LIKE '%fixture%' THEN 'FIXTURE (reason)'
        ELSE 'POLICY'
    END AS Classification,
    Depth
FROM CriteriaChain
ORDER BY Depth DESC;

-- Expected: Current policy (ID 56) -> 55 -> 54 (fixture) -> ... chain includes fixture row 54

-- =============================================================================
-- END OF INVENTORY QUERIES
-- =============================================================================
-- Next steps after authorization:
-- 1. Execute above queries, save results as forensic evidence
-- 2. Full backup/export of NIBRAS_F1_Verify (or affected tables)
-- 3. Decision on document disposition (leave-and-rebaseline vs exact-ID deletion)
-- 4. Create fresh disposable database, apply migrations, verify identity
-- 5. Rerun affected suites on clean database
-- =============================================================================