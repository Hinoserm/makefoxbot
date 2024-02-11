<?php

require_once("../lib/web/lib_default.php");
require_once("../lib/web/lib_login.php");

if (!checkUserLogin())
    exit;

$pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
//$pdo->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);

// Create connection
$conn = new mysqli($servername, $username, $password, $dbname);

// Check connection
if ($conn->connect_error) {
    die("Connection failed: " . $conn->connect_error);
}

ini_set('display_errors', 1);
ini_set('display_startup_errors', 1);
error_reporting(E_ALL);

$now = time();

header('Content-Type: text/csv; charset=utf-8');
header("Content-Disposition: attachment; filename=wait-times-simple-$now.csv");

$hours = isset($_GET['hours']) && is_numeric($_GET['hours']) && $_GET['hours'] > 0 ? $_GET['hours'] : 30;
$div = isset($_GET['div']) && is_numeric($_GET['div']) && $_GET['div'] > 0 ? $_GET['div'] : 10;

$sql = "SELECT
        CONCAT(DATE(date_added), ' ', LPAD(HOUR(date_added), 2, '0'), ':', LPAD(FLOOR(MINUTE(date_added) / $div) * $div, 2, '0'), ':00.00') AS TimeSlot,
	    AVG(UNIX_TIMESTAMP(q.date_worker_start) - UNIX_TIMESTAMP(q.date_added)) AS QueueSec,
        AVG(UNIX_TIMESTAMP(q.date_sent) - UNIX_TIMESTAMP(q.date_worker_start)) AS GPUSec,
        AVG(UNIX_TIMESTAMP(q.date_finished) - UNIX_TIMESTAMP(q.date_sent)) AS UploadSec,
        AVG(UNIX_TIMESTAMP(q.date_finished) - UNIX_TIMESTAMP(q.date_added)) AS TotalSec
FROM
	queue q
WHERE
	q.status = 'FINISHED' AND
        date_added >= NOW() - INTERVAL $hours HOUR AND
        date_sent IS NOT NULL AND
        date_worker_start IS NOT NULL
GROUP BY
    TimeSlot
ORDER BY
    TimeSlot ASC";

/* $sql = "SELECT
    CONCAT(DATE(date_added), ' ', LPAD(HOUR(date_added), 2, '0'), ':', LPAD(FLOOR(MINUTE(date_added) / $div) * $div, 2, '0'), ':00.00') AS TimeSlot,
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY UNIX_TIMESTAMP(q.date_worker_start) - UNIX_TIMESTAMP(q.date_added)) OVER (PARTITION BY TimeSlot) AS QueueSec,
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY UNIX_TIMESTAMP(q.date_sent) - UNIX_TIMESTAMP(q.date_worker_start)) OVER (PARTITION BY TimeSlot) AS GPUSec,
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY UNIX_TIMESTAMP(q.date_finished) - UNIX_TIMESTAMP(q.date_sent)) OVER (PARTITION BY TimeSlot) AS UploadSec,
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY UNIX_TIMESTAMP(q.date_finished) - UNIX_TIMESTAMP(q.date_added)) OVER (PARTITION BY TimeSlot) AS TotalSec
FROM
    queue q
WHERE
    q.status = 'FINISHED' AND
    date_added >= NOW() - INTERVAL $hours HOUR AND
    date_sent IS NOT NULL AND
    date_worker_start IS NOT NULL
GROUP BY
    TimeSlot
ORDER BY
    TimeSlot ASC"; */

$result = $conn->query($sql);

if ($result->num_rows > 0) {
    $fields = $result->fetch_fields();
    $headers = [];
    foreach ($fields as $field) {
        $headers[] = $field->name;
    }
    echo implode(",", $headers) . "\r\n";

    // output data of each row
    while($row = $result->fetch_assoc()) {
        $csvRow = [];
        foreach ($headers as $header) {
            $value = isset($row[$header]) ? $row[$header] : null;
            $csvRow[] = is_null($value) ? NULL : '"' . str_replace('"', '""', $value) . '"';
        }
        echo implode(",", $csvRow) . "\r\n";
    }
} else {
    echo "0 results";
}
$conn->close();
?>
