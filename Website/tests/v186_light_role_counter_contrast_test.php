<?php
$css = file_get_contents(__DIR__ . '/../public/assets/app.css');
$checks = [
    'light group card base' => 'html[data-theme-resolved="light"] .group-card{',
    'founder light gradient' => 'linear-gradient(90deg,#fdecef 0%,#fff 72%)',
    'admin light gradient' => 'linear-gradient(90deg,#fff0f0 0%,#fff 72%)',
    'member light gradient' => 'linear-gradient(90deg,#edf5ff 0%,#fff 72%)',
    'vip light gradient' => 'linear-gradient(90deg,#edf9f1 0%,#fff 72%)',
    'spieler light gradient' => 'linear-gradient(90deg,#fff9df 0%,#fff 72%)',
    'all light gradient' => 'linear-gradient(90deg,#f2f5f8 0%,#fff 72%)',
    'counter light surface' => 'background:rgba(255,255,255,.92)',
    'counter dark text' => 'color:#172033',
    'counter bold' => 'font-weight:850',
];
foreach ($checks as $label => $needle) {
    if (strpos($css, $needle) === false) {
        fwrite(STDERR, "FAIL: {$label}\n");
        exit(1);
    }
}
echo "OK v1.8.6 Light Role Counter Contrast\n";
