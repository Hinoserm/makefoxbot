<?php

require_once("../../../lib/web/lib_default.php");
require_once("../../../lib/web/lib_login.php");

checkUserLogin(true); //In this case it's okay if the user isn't logged in, they'll just get restricted data.

$pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);

if (!isset($_GET['print'])) {
    header('Content-Type: text/csv; charset=utf-8');
    header('Content-Disposition: attachment; filename=users-' . time() . '.csv');
}

$sql = "SELECT u.id,u.access_level,u.telegram_id,u.username,tu.firstname,tu.lastname,tu.is_premium,
               DATE_FORMAT(u.date_added, '%Y-%m-%d %H:%i:%s') AS date_added,
               DATE_FORMAT(u.date_last_seen, '%Y-%m-%d %H:%i:%s') AS date_last_seen
        FROM users u
        LEFT JOIN telegram_users tu ON u.telegram_id = tu.id";

$stmt = $pdo->prepare($sql);
$stmt->execute();

if (isset($user) && isset($user['id']) && $user['access_level'] == 'ADMIN')
    $fields = array("id", "access_level", "telegram_id", "username", "firstname", "lastname", "is_premium", "date_added", "date_last_seen");
else
    $fields = array("id", "date_added", "date_last_seen");

echo implode(",", $fields) . "\r\n";

if ($stmt->rowCount() > 0) {

    // output data of each row
    while ($row = $stmt->fetch(PDO::FETCH_ASSOC)) {
        $csvRow = [];
        foreach ($fields as $field) {
            //if (in_array($field, $allowed_fields))
                $csvRow[] = $row[$field];
            //else
            //    $csvRow[] = null;
        }
        echo implode(",", array_map(function ($value) {
            return '"' . str_replace('"', '""', $value) . '"'; }, $csvRow)) . "\r\n";
    }
}
?>