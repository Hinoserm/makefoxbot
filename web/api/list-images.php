<?php
// images-api.php

require_once("../../lib/web/lib_default.php");
require_once("../../lib/web/lib_login.php");

if (!checkUserLogin())
    exit;

$pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);

ob_start();

$lastImageId = isset($_GET['lastImageId']) ? (int) $_GET['lastImageId'] : 0;
$action = isset($_GET['action']) ? $_GET['action'] : 'old';
$all = isset($_GET['all']) ? (int) $_GET['all'] : 0;

// Define the limit based on the action
$limit = $action === 'new' ? 25 : 10;

$sql = "
SELECT
    q.id, q.uid, q.tele_id, q.tele_chatid, q.type, q.image_id, q.steps, q.seed, q.sampler, q.cfgscale, q.model, q.prompt,
    q.negative_prompt, q.width, q.height, q.denoising_strength, q.selected_image, q.date_added, q.date_finished,
    q.hires_width, q.hires_height, q.hires_enabled, q.hires_denoising_strength, q.hires_steps, q.hires_upscaler,
    IFNULL(w.name, 'Unknown') AS worker_name,
    u.username AS username,
    tu.firstname AS firstname,
    tu.lastname AS lastname
FROM
    queue q
LEFT JOIN users u ON q.uid = u.id
LEFT JOIN telegram_users tu ON u.telegram_id = tu.id
LEFT JOIN workers w ON q.worker = w.id
WHERE
    q.status = 'FINISHED'
";

if ($user['access_level'] != 'ADMIN') {
    $sql .= " AND q.uid = " . $user['id'];
} else if ($user['access_level'] == 'ADMIN' && isset($_GET['uid']) && is_numeric($_GET['uid']) && $_GET['uid'] > 0) {
    $sql .= " AND q.uid = " . (int) $_GET['uid'];
}

if ($all !== 1) {
    if (isset($_GET['id']) && is_numeric($_GET['id']) && $_GET['id'] > 0) {
        $imageId = (int) $_GET['id'];
        $sql .= " AND q.id = $imageId";
        $limit = 1;
    } elseif ($action === 'new' && $lastImageId > 0) {
        $sql .= " AND q.id > :lastImageId";
    } elseif ($lastImageId > 0) {
        $sql .= " AND q.id < :lastImageId";
    }
}

if (isset($_GET['model']) && strlen($_GET['model']) > 0) {
    $imageModel = $_GET['model'];
    $sql .= " AND q.model = :imgModel";
}

if ($all !== 1) {
    $sql .= " ORDER BY q.id DESC LIMIT $limit";
} else {
    $sql .= " ORDER BY q.id DESC";
}

$stmt = $pdo->prepare($sql);

// Conditionally bind the :lastImageId parameter
if ($lastImageId > 0 && $all !== 1) {
    $stmt->bindParam(':lastImageId', $lastImageId, PDO::PARAM_INT);
}

if (isset($_GET['model']) && strlen($_GET['model']) > 0) {
    $stmt->bindParam(':imgModel', $imageModel, PDO::PARAM_STR);
}

// Now execute the statement
$stmt->execute();

$images = [];
while ($row = $stmt->fetch(PDO::FETCH_ASSOC)) {
    $images[$row['id']] = $row; // Assign the entire row to the array

    $images[$row['id']]['imageUrl'] = "/api/get-img.php?imageId=" . $row['image_id'];
}

header('Content-Type: application/json');
echo json_encode(['images' => $images]);
?>
