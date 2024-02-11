<?php

require_once("../../../lib/web/lib_default.php");
require_once("../../../lib/web/lib_login.php");

//checkUserLogin();

$pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);

$hours = isset($_GET['hours']) && is_numeric($_GET['hours']) && $_GET['hours'] > 0 ? $_GET['hours'] : 730;
$div = isset($_GET['div']) && is_numeric($_GET['div']) && $_GET['div'] > 0 ? $_GET['div'] : 12;

//header('Content-Type: text/csv; charset=utf-8');
//header('Content-Disposition: attachment; filename=users.csv');

$sql = "SELECT
    DATE_FORMAT(q.date_finished, '%Y-%m-%d %H:00:00') AS date_added,
    COUNT(DISTINCT uid) AS UniqueUsersCount,
    (SELECT COUNT(*) FROM users u WHERE u.date_added <= q.date_finished) AS TotalUsers,
    COUNT(id) as RequestCount,
    CASE
        WHEN COUNT(DISTINCT uid) > 0 THEN CAST(COUNT(id) AS DECIMAL) / COUNT(DISTINCT uid)
        ELSE 0
    END AS AvgRequestsPerUser
FROM
    queue q
WHERE
    q.status = 'FINISHED' AND
    q.date_finished >= NOW() - INTERVAL :hours HOUR
GROUP BY
    DATE(q.date_finished),
    HOUR(q.date_finished) DIV :div
ORDER BY
    q.date_finished ASC;";

$output = [];

$stmt = $pdo->prepare($sql);
$stmt->execute(['hours' => $hours, 'div' => $div]);

if ($stmt->rowCount() > 0) {
    // output data of each row
    while ($row = $stmt->fetch(PDO::FETCH_ASSOC)) {
        $output[] = $row;
    }

}

header('Content-Type: application/json');
echo json_encode(['user-stats' => $output]);



/*

if ($stmt->rowCount() > 0) {
    $fields = array_map(function ($field) {
        return $field['name'];
    }, $stmt->getColumnMeta(0));
    echo implode(",", $fields) . "\r\n";

    while ($row = $stmt->fetch(PDO::FETCH_ASSOC)) {
        $csvRow = [];
        foreach ($fields as $field) {
            $csvRow[] = $row[$field];
        }
        echo implode(",", array_map(function ($value) {
            return '"' . str_replace('"', '""', $value) . '"'; }, $csvRow)) . "\r\n";
    }
} else {
    echo "0 results";
} */
