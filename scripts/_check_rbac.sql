\echo === Roles ===
SELECT "Id", "Name" FROM roles ORDER BY "Id";

\echo === Users ===
SELECT u."Id", u."Email", u."FullName", r."Name" AS role
FROM users u
LEFT JOIN user_roles ur ON ur."UserId" = u."Id"
LEFT JOIN roles r ON r."Id" = ur."RoleId"
ORDER BY u."Id";

\echo === Suppliers ===
SELECT "Id", "Name", "Status" FROM suppliers ORDER BY "Id";

\echo === Materials ===
SELECT "Id", "Name", "Unit" FROM materials ORDER BY "Id";

\echo === PurchaseOrders ===
SELECT "Id", "Status", "TotalAmount" FROM purchase_orders ORDER BY "Id";
