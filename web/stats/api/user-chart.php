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
    DATE_FORMAT(MIN(q.date_finished), '%Y-%m-%d %H:00:00') AS date_added,
    COUNT(DISTINCT q.uid) AS UniqueUsersCount,
    (SELECT COUNT(*) FROM users u WHERE u.date_added <= MAX(q.date_finished)) AS TotalUsers,
    COUNT(q.id) AS RequestCount,
    CASE
        WHEN COUNT(DISTINCT q.uid) > 0 THEN CAST(COUNT(q.id) AS DECIMAL) / COUNT(DISTINCT q.uid)
        ELSE 0
    END AS AvgRequestsPerUser
FROM
    queue q
WHERE
    q.status = 'FINISHED' AND
    q.date_finished BETWEEN NOW() - INTERVAL :hours HOUR AND NOW() - INTERVAL 1 HOUR
GROUP BY
    FLOOR((UNIX_TIMESTAMP(q.date_finished) - UNIX_TIMESTAMP(NOW() - INTERVAL :hours HOUR)) / (:div * 3600))
ORDER BY
    MIN(q.date_finished) ASC
";

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
