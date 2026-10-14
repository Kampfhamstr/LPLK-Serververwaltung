<?php use App\Core\Security; use App\Core\Auth; ?>
<div class="two-column permission-invite-layout">
<section class="panel">
  <div class="panel-header"><div><h2>Neue Einladung</h2><p>Einladungen sind hierarchisch geschützt. Außer Founder können Nutzer ausschließlich Rollen unterhalb ihrer eigenen Rolle vergeben.</p></div></div>
  <?php if(!$invitableRoles):?>
    <div class="alert alert-warning">Für dieses Konto steht aktuell keine niedrigere einladbare Rolle zur Verfügung.</div>
  <?php else:?>
  <form method="post" action="/invitations" class="form-stack">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <label>E-Mail-Adresse<input type="email" name="email" required autocomplete="off"></label>
    <label>Webpanel-Rolle<select name="web_role">
      <?php foreach($invitableRoles as $key=>$label):?><option value="<?=Security::e($key)?>" <?=$key==='member'?'selected':''?>><?=Security::e($label)?></option><?php endforeach;?>
    </select></label>
    <div class="role-profile-preview">
      <?php foreach($invitableRoles as $key=>$label): $t=Auth::roleTemplate($key);?>
        <div><strong><?=Security::e($label)?></strong><span><?=count(array_filter($t['permissions'],fn($p)=>$p!=='*'))?> Standardrechte · max. CS2-Rolle <?=Security::e(ucfirst((string)$t['max_game_role']))?></span></div>
      <?php endforeach;?>
    </div>

    <?php if(Auth::can('users.permissions')):?>
    <details class="nested-permission-editor"><summary>Optionale individuelle Panelrechte</summary>
      <p class="small-muted">Standard ist „Rollenvorlage“. Einzelne Rechte können bereits bei der Einladung ausdrücklich erlaubt oder verboten werden. Ein ausdrückliches Verbot gewinnt.</p>
      <div class="permission-override-grid invite-permission-grid">
      <?php foreach(Auth::PERMISSIONS as $permission=>$label):$encoded=str_replace('.','__',$permission);$canGrant=Auth::isFounder()||Auth::can($permission);$danger=Auth::isDangerousPermission($permission);?>
        <label class="permission-override-row <?=$danger?'is-dangerous':''?>"><span><?=Security::e($label)?><?php if($danger):?><em>Kritisch</em><?php endif;?><small><?=Security::e($permission)?></small></span><select name="perm_override[<?=Security::e($encoded)?>]"><option value="inherit" selected>Rollenvorlage</option><?php if($canGrant):?><option value="allow">Ausdrücklich erlauben</option><?php endif;?><option value="deny">Ausdrücklich verbieten</option></select></label>
      <?php endforeach;?>
      </div>
    </details>
    <?php endif;?>

    <?php if(Auth::can('users.permissions') && $servers):?>
    <details class="nested-permission-editor"><summary>Optionale Serverabweichungen für die Einladung</summary>
      <p class="small-muted">Standardmäßig übernimmt das neue Konto auf jedem Server seine Rollenvorlage. Nur bei Bedarf einzelne Server abweichend konfigurieren.</p>
      <div class="server-access-grid">
      <?php foreach($servers as $server):$sid=(int)$server['id'];?>
        <article class="server-access-card">
          <strong><?=Security::e($server['name'])?></strong>
          <label>Modus<select name="server_mode[<?=$sid?>]"><option value="inherit" selected>Rollenvorlage verwenden</option><option value="custom">Individuell</option></select></label>
          <div class="permission-check-grid compact">
            <?php foreach(['view'=>'Ansehen','players'=>'Spieler','permissions'=>'Rechte','sync'=>'Sync','live'=>'Live'] as $cap=>$label):?>
              <label class="permission-check"><input type="checkbox" name="server_cap[<?=$sid?>][<?=Security::e($cap)?>]" value="1"> <span><?=Security::e($label)?></span></label>
            <?php endforeach;?>
          </div>
          <label>Max. CS2-Rolle<select name="server_max_game_role[<?=$sid?>]">
            <?php $actorMax=Auth::GAME_ROLE_LEVELS[Auth::serverMaxGameRole($sid)]??1; foreach(Auth::GAME_ROLE_LEVELS as $gr=>$level): if(!Auth::isFounder()&&$level>$actorMax)continue;?>
              <option value="<?=Security::e($gr)?>"><?=Security::e(ucfirst($gr))?></option>
            <?php endforeach;?>
          </select></label>
        </article>
      <?php endforeach;?>
      </div>
    </details>
    <?php endif;?>
    <?php if(Auth::can('users.permissions')):?><label>Grund für individuelle Rechte (nur erforderlich, wenn Abweichungen gesetzt werden)<input type="text" name="access_reason" maxlength="240" placeholder="z. B. Event-Moderation / zuständiger Server"></label><?php endif;?>
    <button class="btn btn-primary" type="submit">Einladung senden</button>
  </form>
  <?php endif;?>
</section>

<section class="panel">
  <div class="panel-header"><div><h2>Einladungsverlauf</h2><p>Auch Widerrufen folgt der Rollenhierarchie.</p></div></div>
  <div class="table-wrap"><table data-ui-adaptive="invitations"><thead><tr><th>E-Mail</th><th>Rolle</th><th>Status</th><th>Eingeladen von</th><th>Erstellt</th><th></th></tr></thead><tbody>
  <?php foreach($invitations as $i):?><?php $status=$i['accepted_at']?'Angenommen':($i['revoked_at']?'Widerrufen':(strtotime($i['expires_at'])<time()?'Abgelaufen':'Ausstehend')); $role=Auth::normalizeWebRole((string)$i['web_role']);?>
    <tr><td><?=Security::e($i['email'])?></td><td><span class="role-pill role-<?=Security::e($role)?>"><?=Security::e(Auth::webRoleLabel($role))?></span></td><td><?=$status?></td><td><?=Security::e($i['inviter'])?></td><td><?=Security::e($i['created_at'])?></td><td>
      <?php if($status==='Ausstehend' && (Auth::isFounder()||Auth::canInviteWebRole($role))):?><form method="post" action="/invitations/<?=$i['id']?>/revoke"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><button class="btn btn-danger btn-small">Widerrufen</button></form><?php endif;?>
    </td></tr>
  <?php endforeach;?></tbody></table></div>
</section>
</div>
