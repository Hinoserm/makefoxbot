<?php

$user = null;

session_set_save_handler(
    function ($save_path, $session_name) {
        // Open
        return true;
    },

    function () {
        // Close
        return true;
    },

    function ($session_id) {
        // Read
		$pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
		$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
		$pdo->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);

        $stmt = $pdo->prepare("SELECT session_data FROM sessions WHERE session_id = :session_id AND (date_deleted IS NULL OR date_deleted > NOW() - INTERVAL 10 MINUTE)");
        $stmt->execute(['session_id' => $session_id]);
        $row = $stmt->fetch();
        return $row ? $row['session_data'] : '';
    },

    function ($session_id, $session_data) {
        // Write
		if (!isset($_SESSION['uid']))
			return true;

		$pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
		$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
		$pdo->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);

        $user_id = isset($_SESSION['uid']) ? $_SESSION['uid'] : null;
        $stmt = $pdo->prepare("REPLACE INTO sessions (session_id, uid, session_data, date_added) VALUES (:session_id, :user_id, :session_data, NOW(3))");
        return $stmt->execute([
            'session_id' => $session_id,
            'session_data' => $session_data,
            'user_id' => $user_id
        ]);
    },

    function ($session_id) {
        // Destroy

		$pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
		$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
		$pdo->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);

        //Was: $stmt = $pdo->prepare("DELETE FROM sessions WHERE session_id = :session_id");

        $stmt = $pdo->prepare("UPDATE sessions SET date_deleted = NOW(2) WHERE session_id = :session_id");
        return $stmt->execute(['session_id' => $session_id]);
    },

    function ($maxlifetime) {
        // Garbage Collection
    
        $pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
        $pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
        $pdo->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);

        $stmt = $pdo->prepare("DELETE FROM sessions WHERE date_deleted < NOW() - INTERVAL 1 DAY LIMIT 5");
        $stmt->execute();

        return true;
    }
);

function checkLogout($silent = false) {
    if (isset($_GET['logout'])) {
        $session_id = session_id();

        setcookie(session_id(), "", time() - 3600);
        session_destroy();
        session_write_close();

        $pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
        $pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
        $pdo->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);

        $stmt = $pdo->prepare("DELETE FROM sessions WHERE session_id = :session_id");
        $stmt->execute(['session_id' => $session_id]);

        unset($_SESSION);
        unset($user);

        //echo "Logged out.\r\n";
        //exit;
    }
}

function checkTelegramAuthorization($auth_data) {
  $check_hash = $auth_data['hash'];
  unset($auth_data['hash']);
  $data_check_arr = [];
  foreach ($auth_data as $key => $value) {
    $data_check_arr[] = $key . '=' . $value;
  }
  sort($data_check_arr);
  $data_check_string = implode("\n", $data_check_arr);
  $secret_key = hash('sha256', BOT_TOKEN, true);
  $hash = hash_hmac('sha256', $data_check_string, $secret_key);
  if (strcmp($hash, $check_hash) !== 0) {
    throw new Exception('Data is NOT from Telegram');
  }
  if ((time() - $auth_data['auth_date']) > 86400) {
    throw new Exception('Data is outdated');
  }
  return $auth_data;
}

function saveTelegramUserData($t, $silent)
{
    global $user;

    $pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
    $pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
    $pdo->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);

    $stmt = $pdo->prepare("SELECT * FROM users WHERE telegram_id = :telegram_id");
    $stmt->execute(['telegram_id' => $t['id']]);
    $u = $stmt->fetch();

    if ($u === false) {
        if (!$silent)
            echo "You are not currently a user of makefoxbot.  Please start a conversation with the bot on Telegram and send /start before logging in here.\r\n";

        return false;
    } else {
        if ($u['access_level'] == 'BANNED') { //Don't allow banned users to do anything.
            header('HTTP/1.1 403 Forbidden');
            exit;

            return false;
        }

        session_regenerate_id(true);

        $_SESSION['uid'] = $u['id'];
        $_SESSION['telegram'] = $t;

        header('Location: ' . $_SERVER['PHP_SELF']);
        exit;

        return true; //Successful login
    }

    return false;
}


function promptUserLogin()
{
    echo '<!DOCTYPE html>';
    echo '<html>';
    echo '<head>';
    echo '<meta charset="utf-8">';
    echo '<title>makefox.bot - Login Required</title>';
    echo '</head>';
    echo '<body><center>';
    echo '<h1>Login required.</h1>';
    echo '<script async src="https://telegram.org/js/telegram-widget.js?2" data-telegram-login="' . BOT_USERNAME . '" data-size="large" data-auth-url="' . htmlspecialchars($_SERVER['PHP_SELF']) . '"></script>';
    echo '</center></body>';
    echo '</html>';
}

function checkUserLogin($silent = false)
{
	global $user;

    register_shutdown_function('session_write_close');
    session_start();

    checkLogout($silent);

    if (isset($_GET['hash']) && isset($_GET['auth_date']) && isset($_GET['id'])) {
        try {
            $auth_data = checkTelegramAuthorization($_GET);
            if (!saveTelegramUserData($auth_data, $silent))
                exit;
        } catch (Exception $e) {
            die($e->getMessage());
        }
    }

    if (!isset($_SESSION) || !isset($_SESSION['uid'])) {
        if (!$silent)
		    promptUserLogin();

		return false;
	}

	if (isset($user))
		return true; //Looks like we already logged in from saveTelegramUserData()

	$pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
	$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
	$pdo->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);

	$stmt = $pdo->prepare("SELECT * FROM users WHERE id = :uid");
	$stmt->execute(['uid' => $_SESSION['uid']]);
	$u = $stmt->fetch();

	if ($u === false) {
		session_destroy(); //Couldn't find that UID, so this session is clearly invalid.  Destroy it.
		unset($_SESSION);  //Not sure if we really need to do this.

        if (!$silent)
		    promptUserLogin();

		return false;
	} else {
		if ($u['access_level'] == 'BANNED') { //Don't allow banned users to log in.
			session_destroy();
			unset($_SESSION);  //Not sure if we really need to do this.

			header('HTTP/1.1 403 Forbidden');
			exit;

			return false;
		}

		$_SESSION['uid'] = $u['id'];
		$user = $u;

		return true; //Successful login
	}

	return false;
}

?>