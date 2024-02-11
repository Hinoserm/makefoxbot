<?php

ini_set('display_errors', 1);
ini_set('display_startup_errors', 1);
error_reporting(E_ALL);


define('BASE_PATH', __DIR__);

// Include the settings
$config = parse_ini_file(BASE_PATH . '/../../conf/settings.ini', true);

// Check if the file was successfully parsed
if ($config === false) {
    die('Error parsing the configuration file.');
}

// Define constants for MySQL settings
define('MYSQL_HOST', $config['mysql']['server']);
define('MYSQL_USERNAME', $config['mysql']['username']);
define('MYSQL_PASSWORD', $config['mysql']['password']);
define('MYSQL_DBNAME', $config['mysql']['database']);

// Define constants for Telegram settings
define('BOT_TOKEN', $config['telegram']['bot_token']);
define('BOT_USERNAME', $config['telegram']['bot_username']);

?>
