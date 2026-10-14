<?php
namespace App\Services;

/** Short-lived, session-only recovery of non-secret fields after a rejected form. */
final class PanelFormDraft
{
    public static function remember(string $path, array $post, int $userId): void
    {
        $fields=[]; $return='';
        if(preg_match('~^/players/([1-9][0-9]*)$~D',$path,$m)) {
            $return=$path.'#verwaltung';
            $allowed=['community_role','status','first_name','last_name','email','phone','discord_name','discord_id','joined_at','left_at','notes'];
        } elseif(preg_match('~^/players/([1-9][0-9]*)/economy/prepare$~D',$path,$m)) {
            $return='/players/'.$m[1].'/economy?server='.(int)($post['server']??0);
            $allowed=['operation','amount','reason'];
        } else return;
        if($userId<1)return;
        foreach($allowed as $name) if(isset($post[$name])&&is_string($post[$name])&&strlen($post[$name])<=4000) $fields[$name]=$post[$name];
        $_SESSION['_panel_form_draft']=['user'=>$userId,'path'=>$path,'return'=>$return,'fields'=>$fields,'expires'=>time()+600];
    }

    public static function take(string $path,int $userId): ?array
    {
        $draft=$_SESSION['_panel_form_draft']??null;
        if(!$draft)return null;
        if(($draft['user']??0)!==$userId || ($draft['expires']??0)<time()){unset($_SESSION['_panel_form_draft']);return null;}
        if(parse_url($draft['return'],PHP_URL_PATH)!==$path)return null;
        unset($_SESSION['_panel_form_draft']);return $draft;
    }
}
