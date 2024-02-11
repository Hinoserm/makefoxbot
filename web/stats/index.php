<?php
// Get a list of PHP files in the current directory
$files = array_merge(glob('*.php'), glob('*.html'));
;

// HTML header
echo "<!DOCTYPE html>\n";
echo "<html lang='en'>\n<head>\n";
echo "<meta charset='UTF-8'>\n";
echo "<title>Available Charts</title>\n";
echo "</head>\n<body>\n";
echo "<h1>Available Charts:</h1>\n";
echo "<ul>\n";

// Generate a list item for each PHP file
foreach ($files as $file) {
    // Exclude this script from the list
    if ($file === basename(__FILE__)) {
        continue;
    }
    echo "<li><a href='$file'>$file</a></li>\n";
}

// HTML footer
echo "</ul>\n";
echo "</body>\n</html>";
?><?php
