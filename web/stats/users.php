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

if (isset($_SERVER['PHP_AUTH_USER']) && isset($_SERVER['PHP_AUTH_PW'])) {
    $username = $_SERVER['PHP_AUTH_USER'];
    $password = $_SERVER['PHP_AUTH_PW'];

    // Verify credentials
    if ($username == "user" && $password == "crabcakes22") {
        // Credentials are correct
        // Your protected content goes here
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
header('Content-Disposition: attachment; filename=users.csv');

$sql = "SELECT u.id,u.access_level,u.telegram_id,u.username,tu.firstname,tu.lastname,tu.is_premium,
               DATE_FORMAT(u.date_added, '%Y-%m-%d %H:%i:%s') AS date_added,
               DATE_FORMAT(u.date_last_seen, '%Y-%m-%d %H:%i:%s') AS date_last_seen
        FROM users u
        LEFT JOIN telegram_users tu ON u.telegram_id = tu.id";

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
