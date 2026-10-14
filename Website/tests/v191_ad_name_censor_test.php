<?php
$root=dirname(__DIR__);
$t=file_get_contents($root.'/templates/server_live.php');
if(!str_contains($t,'SerververwaltungAntiAdvertisement v1.0.1')){fwrite(STDERR,"AntiAd 1.0.1 Hinweis fehlt\n");exit(1);}
if(!str_contains($t,'neutralen Namen zensiert')){fwrite(STDERR,"Namenszensur-Hinweis fehlt\n");exit(1);}
echo "v1.9.1 Werbenamen-Zensur Integration erfolgreich.\n";
