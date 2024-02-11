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

if (isset($_SERVER['PHP_AUTH_USER']) && isset($_SERVER['PHP_AUTH_PW'])) {
    $username = $_SERVER['PHP_AUTH_USER'];
    $password = $_SERVER['PHP_AUTH_PW'];

    // Verify credentials
    if ($username == "user" && $password == "crabcakes22") {


    } else {
        // If credentials are wrong
        header('WWW-Authenticate: Basic realm="My Protected Area"');
        header('HTTP/1.0 401 Unauthorized');
        echo "Invalid username or password.";
        exit;
    }
} else {
    // The user has not sent any credentials
    header('WWW-Authenticate: Basic realm="My Protected Area"');
    header('HTTP/1.0 401 Unauthorized');
    echo "Authentication required.";
    exit;
}


header('Content-Type: text/csv; charset=utf-8');
header('Content-Disposition: attachment; filename=wait_times.csv');

$days = isset($_GET['days']) && is_numeric($_GET['days']) && $_GET['days'] > 0 ? $_GET['days'] : 30;

$sql = "SELECT
	DATE_FORMAT(q.date_added, '%Y-%m-%d %H:%i:%s.%f') as date_added,
	q.type,
	q.uid,
	q.steps,
	q.width,
	q.height,
	q.denoising_strength,
	q.cfgscale,
	q.error_str,
	q.worker,
	(UNIX_TIMESTAMP(q.date_worker_start) - UNIX_TIMESTAMP(q.date_added)) AS QueueSec,
        (UNIX_TIMESTAMP(q.date_sent) - UNIX_TIMESTAMP(q.date_worker_start)) AS GPUSec,
        (UNIX_TIMESTAMP(q.date_finished) - UNIX_TIMESTAMP(q.date_sent)) AS UploadSec,
        (UNIX_TIMESTAMP(q.date_finished) - UNIX_TIMESTAMP(q.date_added)) AS TotalSec
FROM
	queue q
WHERE
	q.status = 'FINISHED'
	AND q.date_added >= NOW() - INTERVAL $days DAY";

$result = $conn->query($sql);

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
