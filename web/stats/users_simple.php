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

//ini_set('display_errors', 1);
//ini_set('display_startup_errors', 1);
//error_reporting(E_ALL);

$hours = isset($_GET['hours']) && is_numeric($_GET['hours']) && $_GET['hours'] > 0 ? $_GET['hours'] : 730;
$div = isset($_GET['div']) && is_numeric($_GET['div']) && $_GET['div'] > 0 ? $_GET['div'] : 12;

header('Content-Type: text/csv; charset=utf-8');
header('Content-Disposition: attachment; filename=users.csv');

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
	q.date_finished >= NOW() - INTERVAL $hours HOUR
GROUP BY
    DATE(q.date_finished),
    HOUR(q.date_finished) DIV $div
ORDER BY
    q.date_finished ASC;";


			//DIV @interval_hours

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
            $csvRow[] = $row[$header];
        }
        echo implode(",", array_map(function($value) { return '"' . str_replace('"', '""', $value) . '"'; }, $csvRow)) . "\r\n";
    }
} else {
    echo "0 results";
}
$conn->close();
?>
