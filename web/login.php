<?php

require_once("../lib/web/lib_default.php");
require_once("../lib/web/lib_login.php");

if (!checkUserLogin())
	exit;

//print_r ($user);

echo "Welcome user " . $user['id'] . "\r\n";

?>