<?php

require_once("../../../lib/web/lib_default.php");
require_once("../../../lib/web/lib_login.php");

if (!checkUserLogin())
    exit;

if (!isset($user['id']) || $user['access_level'] != 'ADMIN')
    exit; //Admins only.

$pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
$pdo->setAttribute(PDO::MYSQL_ATTR_USE_BUFFERED_QUERY, false);

header('Content-Type: text/csv; charset=utf-8');
header('Content-Disposition: attachment; filename=gpu_stats.csv');

$days = isset($_GET['days']) && is_numeric($_GET['days']) && $_GET['days'] > 0 ? $_GET['days'] : 30;

$sql = "SELECT
    DATE_FORMAT(g.`when`, '%Y-%m-%d %H:%i:%s.%f') as `when`,
    g.uuid,
    g.model_name,
    g.serial_number,
    g.temperature,
    g.gpu_utilization,
    g.mem_utilization,
    g.power_usage,
    g.fan_speed,
    g.sm_clock,
    g.gpu_clock,
    g.mem_clock,
    g.power_state,
    g.mem_total,
    g.mem_used,
    g.mem_free,
    g.pcie_rx,
    g.pcie_tx,
    g.driver_version
FROM
    gpu_stats g
WHERE
    g.`when` >= NOW() - INTERVAL :days DAY";

$stmt = $pdo->prepare($sql);
$stmt->bindParam(':days', $days, PDO::PARAM_INT);
$stmt->execute();

// Assuming you have already sent headers for CSV output

// Fetch column headers
$columnCount = $stmt->columnCount();
$headers = [];
for ($i = 0; $i < $columnCount; $i++) {
    $meta = $stmt->getColumnMeta($i);
    $headers[] = $meta['name'];
}
echo implode(",", $headers) . "\r\n";

// Fetch rows one by one
while ($row = $stmt->fetch(PDO::FETCH_ASSOC)) {
    $csvRow = [];
    foreach ($row as $value) {
        $csvRow[] = '"' . str_replace('"', '""', $value) . '"';
    }
    echo implode(",", $csvRow) . "\r\n";
}

// Remember to close the cursor if you need to perform other queries using the same connection
$stmt->closeCursor();
?>
