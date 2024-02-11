<?php

require_once("../../lib/web/lib_default.php");
require_once("../../lib/web/lib_login.php");

checkUserLogin(true); //In this case it's okay if the user isn't logged in, they'll just get restricted data.

$pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);

$sql = "SELECT u.id,u.access_level,u.telegram_id,u.username,tu.firstname,tu.lastname,tu.is_premium,
               DATE_FORMAT(u.date_added, '%Y-%m-%d %H:%i:%s') AS date_added,
               DATE_FORMAT(u.date_last_seen, '%Y-%m-%d %H:%i:%s') AS date_last_seen
        FROM users u
        LEFT JOIN telegram_users tu ON u.telegram_id = tu.id";

$stmt = $pdo->prepare($sql);
$stmt->execute();



$allowed_fields = array("id", "date_added", "date_last_seen");

if (isset($user) && isset($user['id']) && $user['access_level'] == 'ADMIN')
    unset($allowed_fields);



$users = [];



if ($stmt->rowCount() > 0) {

    // output data of each row
    while ($row = $stmt->fetch(PDO::FETCH_ASSOC)) {
        $output = [];

        foreach ($row as $key => $value) {
            if (!isset($allowed_fields) || in_array($key, $allowed_fields))
                $output[$key] = $row[$key];
        }

        $users[$output['id']] = $output;
    }

}


header('Content-Type: application/json');
echo json_encode(['users' => $users]);

?>