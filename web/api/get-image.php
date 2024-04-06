<?php
// serve-image.php

require_once("../../lib/web/lib_default.php");
require_once("../../lib/web/lib_login.php");

if (!checkUserLogin())
	exit;

$pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
//$pdo->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);

ob_start();

$imageId = isset($_GET['id']) ? (int)$_GET['id'] : 0;

if ($imageId > 0) {
	if ($user['access_level'] == 'ADMIN') {
		$stmt = $pdo->prepare("SELECT image_file FROM images WHERE id = :imageId");
		$stmt->execute(['imageId' => $imageId]);
	} else {
		$stmt = $pdo->prepare("SELECT image_file FROM images WHERE id = :imageId AND user_id = :uid");
		$stmt->execute(['imageId' => $imageId, 'uid' => $user['id']]);
	}

    $imageFile = $stmt->fetchColumn();

	if ($imageFile !== false) {
        $imageData = file_get_contents("../../data/" . $imageFile);
    }

    if ($imageData !== false) {
		if (!isset($_GET['full']) || !$_GET['full']) {
			$sourceImage = imagecreatefromstring($imageData);
			if ($sourceImage !== false) {
				$originalWidth = imagesx($sourceImage);
				$originalHeight = imagesy($sourceImage);
				$newWidth = round($originalWidth * 0.5);
				$newHeight = round($originalHeight * 0.5);

				$resizedImage = imagecreatetruecolor($newWidth, $newHeight);
				imagecopyresampled($resizedImage, $sourceImage, 0, 0, 0, 0, $newWidth, $newHeight, $originalWidth, $originalHeight);

				header('Content-Type: image/jpeg');
				imagejpeg($resizedImage);
				imagedestroy($sourceImage);
				imagedestroy($resizedImage);
			} else {
				header('Content-Type: image/png');
				echo $imageData;
			}
		} else {
			header('Content-Type: image/png');
			echo $imageData;
		}
    }

	exit;
}
header("HTTP/1.0 404 Not Found");

?>
