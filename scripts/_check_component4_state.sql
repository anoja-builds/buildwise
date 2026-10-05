\echo === MigrationsApplied ===
SELECT "MigrationId", "ProductVersion" FROM public."__EFMigrationsHistory" ORDER BY 1;

\echo === RowCounts ===
SELECT 'users' AS t, count(*) FROM users
UNION ALL SELECT 'projects', count(*) FROM projects
UNION ALL SELECT 'materials', count(*) FROM materials
UNION ALL SELECT 'material_requests', count(*) FROM material_requests
UNION ALL SELECT 'suppliers', count(*) FROM suppliers
UNION ALL SELECT 'quotations', count(*) FROM quotations
UNION ALL SELECT 'purchase_orders', count(*) FROM purchase_orders
UNION ALL SELECT 'deliveries', count(*) FROM deliveries
UNION ALL SELECT 'delivery_items', count(*) FROM delivery_items
UNION ALL SELECT 'inspections', count(*) FROM inspections
UNION ALL SELECT 'inspection_items', count(*) FROM inspection_items
UNION ALL SELECT 'non_conformances', count(*) FROM non_conformances
ORDER BY 1;
