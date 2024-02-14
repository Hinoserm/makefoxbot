<?php

require_once("../../../lib/web/lib_default.php");
require_once("../../../lib/web/lib_login.php");

if (!checkUserLogin())
    exit;

if ($user['access_level'] != "ADMIN")
    exit; //Admins only for now.

$pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
$pdo->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);

ini_set('display_errors', 1);
ini_set('display_startup_errors', 1);
error_reporting(E_ALL);

header('Content-Type: text/csv; charset=utf-8');
header('Content-Disposition: attachment; filename=wait_times.csv');

$days = isset($_GET['days']) && is_numeric($_GET['days']) && $_GET['days'] > 0 ? $_GET['days'] : 9999;

$sql = "SELECT
    DATE_FORMAT(q.date_added, '%Y-%m-%d %H:%i:%s.%f') as date_added,
    q.type,
    q.uid,
    u.username AS username,
    tu.firstname AS firstname,
    tu.lastname AS lastname,
    q.steps,
    q.width,
    q.height,
    q.denoising_strength,
    q.cfgscale,
    q.error_str,
    IFNULL(q.model, 'indigoFurryMix_v105Hybrid') AS model,
    IFNULL(w.name, 'Unknown') AS worker_name,
    (UNIX_TIMESTAMP(q.date_worker_start) - UNIX_TIMESTAMP(q.date_added)) AS QueueSec,
    (UNIX_TIMESTAMP(q.date_sent) - UNIX_TIMESTAMP(q.date_worker_start)) AS GPUSec,
    (UNIX_TIMESTAMP(q.date_finished) - UNIX_TIMESTAMP(q.date_sent)) AS UploadSec,
    (UNIX_TIMESTAMP(q.date_finished) - UNIX_TIMESTAMP(q.date_added)) AS TotalSec
FROM
    queue q
LEFT JOIN users u ON q.uid = u.id
LEFT JOIN telegram_users tu ON u.telegram_id = tu.id
LEFT JOIN workers w ON q.worker = w.id
WHERE
    q.status = 'FINISHED'
    AND q.date_added >= NOW() - INTERVAL $days DAY";

$stmt = $pdo->prepare($sql);
$stmt->execute();

$result = $stmt->fetchAll();

if (count($result) > 0) {
    $headers = array_keys($result[0]);
    echo implode(",", $headers) . "\r\n";

    // output data of each row
    foreach ($result as $row) {
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
?>
