<?php

require_once("../../../lib/web/lib_default.php");
require_once("../../../lib/web/lib_login.php");

//checkUserLogin();

$pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
$pdo->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);

$now = time();

// header content if needed.
// header('Content-Type: text/csv; charset=utf-8');
// header("Content-Disposition: attachment; filename=wait-times-simple-$now.csv");

$hours = isset($_GET['hours']) && is_numeric($_GET['hours']) && $_GET['hours'] > 0 ? $_GET['hours'] : 30;
$div = isset($_GET['div']) && is_numeric($_GET['div']) && $_GET['div'] > 0 ? $_GET['div'] : 10;

$sql = "SELECT
        CONCAT(DATE(date_added), ' ', LPAD(HOUR(date_added), 2, '0'), ':',
               LPAD(FLOOR(MINUTE(date_added) / :div) * :div, 2, '0'), ':00.00') AS TimeSlot,
        AVG(UNIX_TIMESTAMP(q.date_worker_start) - UNIX_TIMESTAMP(CASE
               WHEN q.retry_date IS NOT NULL THEN q.retry_date
               ELSE q.date_added
           END)) AS QueueSec,
        AVG(CASE
               WHEN q.retry_date IS NOT NULL THEN UNIX_TIMESTAMP(q.retry_date) - UNIX_TIMESTAMP(q.date_added)
               ELSE 0
           END) AS WaitTimeSec,
        AVG(UNIX_TIMESTAMP(q.date_sent) - UNIX_TIMESTAMP(q.date_worker_start)) AS GPUSec,
        AVG(UNIX_TIMESTAMP(q.date_finished) - UNIX_TIMESTAMP(q.date_sent)) AS UploadSec,
        AVG(UNIX_TIMESTAMP(q.date_finished) - UNIX_TIMESTAMP(CASE
               WHEN q.retry_date IS NOT NULL THEN q.retry_date
               ELSE q.date_added
           END)) AS TotalSec
    FROM
        queue q
    WHERE
        q.status = 'FINISHED' AND
        date_added >= NOW() - INTERVAL :hours HOUR AND
        date_sent IS NOT NULL AND
        date_worker_start IS NOT NULL
    GROUP BY
        TimeSlot
    ORDER BY
        TimeSlot ASC";

$stmt = $pdo->prepare($sql);
$stmt->bindParam(':hours', $hours, PDO::PARAM_INT);
$stmt->bindParam(':div', $div, PDO::PARAM_INT);
$stmt->execute();

$output = [];

if ($stmt->rowCount() > 0) {
    // output data of each row
    while ($row = $stmt->fetch(PDO::FETCH_ASSOC)) {
        $output[] = $row;
    }
}

header('Content-Type: application/json');
echo json_encode(['user-stats' => $output]);
?>
