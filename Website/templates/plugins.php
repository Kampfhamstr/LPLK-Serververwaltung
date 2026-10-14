<?php use App\Core\Auth; use App\Core\Security; ?>

<section class="panel plugin-overview-head">
  <div class="panel-header split-header">
    <div>
      <h2>Addons & Updates</h2>
      <p>Die kompakte Ansicht zeigt zunächst nur das Wesentliche. Server und Plugins lassen sich gezielt aufklappen.</p>
    </div>
    <div class="plugin-summary"><strong><?=$updateCount?></strong><span>Updates verfügbar</span></div>
  </div>

  <div class="plugin-browser-toolbar">
    <input type="search" placeholder="Plugin, ID, Autor oder Repository suchen…" data-plugin-search>
    <button class="btn btn-secondary btn-small" type="button" data-plugin-open-updates>Updates öffnen</button>
    <button class="btn btn-secondary btn-small" type="button" data-plugin-collapse-all>Alles einklappen</button>
  </div>

  <?php if(Auth::can('plugins.repository.manage')):?>
  <div class="toolbar top-gap">
    <form method="post" action="/plugins/check-all">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
      <button class="btn btn-primary">Alle GitHub-Quellen prüfen</button>
    </form>
    <a class="btn btn-secondary" href="/settings#github">GitHub API Einstellungen</a>
  </div>
  <p class="small-muted">Auto-Updates laufen nur für explizit freigegebene Quellen im Modus Backup + Reload. Ohne GitHub SHA-256-Digest wird kein automatisches Deployment gestartet.</p>
  <?php endif;?>
</section>

<section class="panel plugin-catalog-panel">
  <div class="panel-header split-header">
    <div><h2>Plugin-Katalog</h2><p>Jedes erkannte Plugin erscheint hier genau einmal – mit Repository-Status und allen Serverinstallationen.</p></div>
    <div class="plugin-repo-summary">
      <span class="repo-state repo-configured"><?= (int)($repoCounts['configured']??0) ?> Repo</span>
      <span class="repo-state repo-detected"><?= (int)($repoCounts['detected']??0) ?> erkannt</span>
      <span class="repo-state repo-missing"><?= (int)($repoCounts['missing']??0) ?> fehlen</span>
    </div>
  </div>
  <div class="plugin-catalog-grid" data-plugin-catalog>
  <?php foreach(($catalog??[]) as $item):?>
    <?php
      $state=(string)$item['repo_state'];
      $source=$item['source']??null;
      $suggested=$item['suggested_repository']??null;
      $catalogSearch=strtolower(implode(' ',[(string)$item['name'],(string)$item['id'],(string)$item['author'],(string)($source['repository']??$suggested??''),implode(' ',(array)$item['versions'])]));
    ?>
    <article class="plugin-catalog-card repo-<?=$state?><?=!empty($item['update_available'])?' has-update':''?><?=!empty($item['version_conflict'])?' has-version-conflict':''?>" data-plugin-catalog-card data-search="<?=Security::e($catalogSearch)?>">
      <div class="plugin-catalog-head">
        <div><strong><?=Security::e((string)$item['name'])?></strong><span class="mono"><?=Security::e((string)$item['id'])?></span></div>
        <div class="plugin-catalog-badges">
          <?php if($state==='configured'):?><span class="repo-state repo-configured">REPO</span><?php elseif($state==='detected'):?><span class="repo-state repo-detected">REPO ERKANNT</span><?php elseif($state==='missing'):?><span class="repo-state repo-missing">REPO FEHLT</span><?php else:?><span class="repo-state repo-manual">MANUELL</span><?php endif;?>
          <?php if(!empty($item['update_available'])):?><span class="status-pill plugin-update-pill">UPDATE</span><?php endif;?>
          <?php if(!empty($item['version_conflict'])):?><span class="repo-state repo-conflict">VERSIONEN ABWEICHEND</span><?php endif;?>
        </div>
      </div>
      <div class="plugin-catalog-installs">
        <?php foreach($item['installations'] as $inst):?>
          <span><strong><?=Security::e((string)$inst['server_name'])?></strong> <?=Security::e((string)($inst['version']?:'—'))?> · <?=Security::e((string)$inst['status'])?><?=$inst['update_available']?' · Update':''?></span>
        <?php endforeach;?>
      </div>
      <?php if($source):?>
        <div class="plugin-repo-links catalog-repo-links">
          <?php if(!empty($item['repository_url'])):?><a href="<?=Security::e((string)$item['repository_url'])?>" target="_blank" rel="noopener noreferrer">Repository ↗</a><?php endif;?>
          <?php if(!empty($source['latest_release_url'])):?><a href="<?=Security::e((string)$source['latest_release_url'])?>" target="_blank" rel="noopener noreferrer">Release ↗</a><?php endif;?>
        </div>
      <?php elseif($state==='detected'):?>
        <div class="catalog-repo-detected mono"><?=Security::e((string)$suggested)?></div>
      <?php endif;?>
      <?php if(Auth::can('plugins.repository.manage')&&in_array($state,['missing','detected'],true)):?>
        <details class="catalog-source-setup"><summary>Repository hinterlegen</summary>
          <form method="post" action="/plugins/<?=rawurlencode((string)$item['id'])?>/source" class="catalog-source-form">
            <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
            <input type="text" name="repository" required value="<?=Security::e((string)($suggested??''))?>" placeholder="owner/repository">
            <input type="hidden" name="asset_pattern" value="*.zip"><input type="hidden" name="channel" value="stable"><input type="hidden" name="update_mode" value="check_only">
            <button class="btn btn-primary btn-small">Speichern & prüfen</button>
          </form>
        </details>
      <?php endif;?>
    </article>
  <?php endforeach;?>
  <?php if(empty($catalog)):?><div class="muted">Noch keine Plugins im Inventar.</div><?php endif;?>
  </div>
</section>

<div class="plugin-server-grid">
<?php foreach($servers as $server):?>
  <?php
    $inventory=$server['inventory'];
    $serverPlugins=(array)($inventory['plugins']??[]);
    $serverUpdateCount=count(array_filter(
      $serverPlugins,
      static fn(array $plugin):bool=>!empty($plugin['update_available'])
    ));
  ?>
  <details
    class="panel plugin-server-accordion"
    id="server-<?=(int)$server['id']?>" data-plugin-server
    data-server-name="<?=Security::e(strtolower((string)$server['name']))?>">
    <summary class="plugin-server-summary">
      <span class="plugin-server-summary-main">
        <strong><?=Security::e($server['name'])?></strong>
        <small>Bridge <?=Security::e((string)($inventory['bridge_version']??'—'))?> · <?=$inventory['bridge_online']?'online':'offline'?></small>
      </span>
      <span class="plugin-server-summary-stats">
        <span class="status-pill"><?=count($serverPlugins)?> Plugins</span>
        <?php if($serverUpdateCount):?><span class="status-pill plugin-update-pill"><?=$serverUpdateCount?> Updates</span><?php endif;?>
        <span class="plugin-disclosure">Aufklappen</span>
      </span>
    </summary>

    <div class="plugin-server-body">
      <?php if(Auth::can('plugins.rescan')&&$inventory['bridge_online']):?>
      <div class="toolbar plugin-server-tools">
        <form method="post" action="/plugins/server/<?=(int)$server['id']?>/rescan">
          <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
          <button class="btn btn-secondary btn-small">Plugin-Inventar neu laden</button>
        </form>
      </div>
      <?php endif;?>

      <?php if(!$inventory['bridge_online']):?>
        <div class="alert alert-warning"><?=Security::e((string)($inventory['reason']??'LiveBridge nicht erreichbar.'))?></div>
      <?php elseif(empty($serverPlugins)):?>
        <div class="muted plugin-empty">Die Bridge meldet aktuell keine SwiftlyS2 Plugins.</div>
      <?php else:?>
        <div class="plugin-card-grid">
        <?php foreach($serverPlugins as $plugin):?>
          <?php
            $pid=(string)($plugin['id']??'');
            $source=$plugin['update_source']??null;
            $suggested=$plugin['suggested_repository']??null;
            $self=$pid==='lplk.serververwaltung.bridge';
            $repoUrl=(string)($plugin['github_repository_url']??'');
            $searchText=strtolower(implode(' ',[
              (string)($plugin['name']??''),
              $pid,
              (string)($plugin['author']??''),
              (string)($source['repository']??$suggested??''),
              (string)($plugin['version']??''),
            ]));
          ?>
          <details
            class="plugin-card<?=!empty($plugin['update_available'])?' has-update':''?>"
            id="plugin-<?=Security::e($pid)?>"
            data-plugin-card
            data-update-available="<?=!empty($plugin['update_available'])?'1':'0'?>"
            data-search="<?=Security::e($searchText)?>">
            <summary class="plugin-card-summary">
              <span class="plugin-card-title">
                <strong><?=Security::e((string)($plugin['name']??$pid))?></strong>
                <small class="mono"><?=Security::e($pid)?></small>
              </span>
              <span class="plugin-card-quick">
                <span class="plugin-version"><?=Security::e((string)($plugin['version']??'—'))?></span>
                <span class="status-pill <?=$plugin['status']==='Loaded'?'status-online':''?>"><?=Security::e((string)($plugin['status']??'Indeterminate'))?></span>
                <?php if($source):?>
                  <?php if($repoUrl!==''):?><a class="repo-state repo-configured plugin-quick-repo" href="<?=Security::e($repoUrl)?>" target="_blank" rel="noopener noreferrer">REPO ↗</a><?php else:?><span class="repo-state repo-configured">REPO</span><?php endif;?>
                  <?php if(!empty($source['latest_release_url'])):?><a class="repo-state repo-release plugin-quick-repo" href="<?=Security::e((string)$source['latest_release_url'])?>" target="_blank" rel="noopener noreferrer">RELEASE ↗</a><?php endif;?>
                <?php elseif($suggested):?>
                  <?php if($repoUrl!==''):?><a class="repo-state repo-detected plugin-quick-repo" href="<?=Security::e($repoUrl)?>" target="_blank" rel="noopener noreferrer">REPO ERKANNT ↗</a><?php else:?><span class="repo-state repo-detected">REPO ERKANNT</span><?php endif;?>
                <?php elseif($self):?><span class="repo-state repo-manual">MANUELL</span>
                <?php else:?><span class="repo-state repo-missing">REPO FEHLT</span><?php endif;?>
                <?php if(!empty($plugin['update_available'])):?><span class="status-pill plugin-update-pill">UPDATE</span><?php endif;?>
              </span>
            </summary>

            <div class="plugin-card-body">
              <div class="plugin-detail-grid">
                <section class="plugin-detail-box">
                  <h3>Installiert</h3>
                  <dl class="plugin-kv">
                    <dt>Version</dt><dd><?=Security::e((string)($plugin['version']??'—'))?></dd>
                    <dt>Status</dt><dd><?=Security::e((string)($plugin['status']??'Indeterminate'))?></dd>
                    <?php if(!empty($plugin['author'])):?><dt>Autor</dt><dd><?=Security::e((string)$plugin['author'])?></dd><?php endif;?>
                    <?php if(!empty($plugin['minimum_api_version'])):?><dt>API</dt><dd>≥ <?=Security::e((string)$plugin['minimum_api_version'])?></dd><?php endif;?>
                  </dl>
                  <?php if(!empty($plugin['path'])):?><div class="plugin-path-full mono"><?=Security::e((string)$plugin['path'])?></div><?php endif;?>
                  <?php if(!empty($plugin['website'])&&str_starts_with((string)$plugin['website'],'https://')):?><a class="plugin-inline-link" href="<?=Security::e((string)$plugin['website'])?>" target="_blank" rel="noopener noreferrer">Plugin-Website ↗</a><?php endif;?>
                </section>

                <section class="plugin-detail-box">
                  <h3>GitHub / Version</h3>
                  <?php if($source):?>
                    <strong class="plugin-repository-name"><?=Security::e((string)$source['repository'])?></strong>
                    <div class="plugin-repo-links">
                      <?php if($repoUrl!==''):?><a href="<?=Security::e($repoUrl)?>" target="_blank" rel="noopener noreferrer">GitHub Repository ↗</a><?php endif;?>
                      <?php if(!empty($source['latest_release_url'])):?><a href="<?=Security::e((string)$source['latest_release_url'])?>" target="_blank" rel="noopener noreferrer">Letztes Release ↗</a><?php endif;?>
                    </div>
                    <dl class="plugin-kv">
                      <dt>Latest</dt><dd><?=Security::e((string)($source['latest_version']?:$source['latest_tag']?:'noch nicht geprüft'))?></dd>
                      <dt>Asset</dt><dd><?=Security::e((string)($source['latest_asset_name']?:$source['asset_pattern']))?></dd>
                      <dt>SHA-256</dt><dd><?=!empty($source['latest_asset_sha256'])?'vorhanden':'nicht von GitHub geliefert'?></dd>
                    </dl>
                    <?php if(!empty($source['last_error'])):?><div class="text-danger small-muted"><?=Security::e((string)$source['last_error'])?></div><?php endif;?>
                  <?php elseif($suggested):?>
                    <span class="small-muted">GitHub aus Plugin-Metadaten erkannt</span>
                    <div class="mono plugin-repository-name"><?=Security::e($suggested)?></div>
                    <?php if($repoUrl!==''):?><div class="plugin-repo-links"><a href="<?=Security::e($repoUrl)?>" target="_blank" rel="noopener noreferrer">GitHub Repository ↗</a></div><?php endif;?>
                  <?php else:?>
                    <span class="muted">Keine GitHub-Quelle hinterlegt oder erkannt.</span>
                  <?php endif;?>
                </section>

                <section class="plugin-detail-box plugin-actions-box">
                  <h3>Update / Aktionen</h3>
                  <?php if($self):?>
                    <span class="muted">Bridge-Update bewusst manuell</span>
                  <?php elseif($plugin['update_available']):?>
                    <div class="update-available"><strong><?=Security::e((string)$plugin['version'])?> → <?=Security::e((string)$plugin['latest_version'])?></strong></div>
                    <?php if(Auth::can('plugins.update')&&$source&&$source['update_mode']==='reload'):?>
                      <form method="post" action="/plugins/server/<?=(int)$server['id']?>/<?=rawurlencode($pid)?>/install" data-confirm="Plugin wirklich aktualisieren? Vorher wird automatisch ein Backup angelegt; bei Ladefehler erfolgt ein Rollback.">
                        <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
                        <button class="btn btn-primary btn-small">Update installieren</button>
                      </form>
                    <?php else:?><span class="small-muted">Installation nicht freigegeben</span><?php endif;?>
                  <?php else:?>
                    <?php if(!$source):?><span class="status-pill">Nicht geprüft</span><?php else:?><span class="status-pill status-online">Aktuell</span><?php endif;?>
                  <?php endif;?>

                  <?php if(Auth::can('plugins.rollback')&&!empty($plugin['latest_backup_id'])&&!$self):?>
                    <form method="post" action="/plugins/server/<?=(int)$server['id']?>/<?=rawurlencode($pid)?>/rollback" class="top-gap-small" data-confirm="Plugin wirklich auf das letzte Bridge-Backup zurückrollen?">
                      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
                      <input type="hidden" name="backup_id" value="<?=Security::e((string)$plugin['latest_backup_id'])?>">
                      <button class="btn btn-secondary btn-small">Rollback <?=Security::e((string)$plugin['latest_backup_id'])?></button>
                    </form>
                  <?php endif;?>
                </section>
              </div>

              <?php if(Auth::can('plugins.repository.manage')&&!$self):?>
              <details class="plugin-source-config">
                <summary>GitHub-Updatequelle konfigurieren</summary>
                <div class="plugin-source-config-body">
                  <form method="post" action="/plugins/<?=rawurlencode($pid)?>/source" class="form-stack plugin-source-form">
                    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
                    <div class="form-grid">
                      <label>Repository<input type="text" name="repository" required value="<?=Security::e((string)($source['repository']??$suggested??''))?>" placeholder="owner/repository"></label>
                      <label>Release-Asset<input type="text" name="asset_pattern" required value="<?=Security::e((string)($source['asset_pattern']??'*.zip'))?>" placeholder="Plugin_*.zip oder Whitelist-v*.rar"></label>
                      <label>Installations-Unterpfad im Archiv<input type="text" name="install_subpath" value="<?=Security::e((string)($source['install_subpath']??''))?>" placeholder="leer = automatisch"></label>
                      <label>Kanal<select name="channel"><option value="stable" <?=($source['channel']??'stable')==='stable'?'selected':''?>>Stable</option><option value="prerelease" <?=($source['channel']??'')==='prerelease'?'selected':''?>>Stable + Prerelease</option></select></label>
                      <label>Update-Modus<select name="update_mode"><option value="check_only" <?=($source['update_mode']??'check_only')==='check_only'?'selected':''?>>Nur Version prüfen</option><option value="reload" <?=($source['update_mode']??'')==='reload'?'selected':''?>>Backup + kontrollierter Reload</option></select></label>
                      <label class="checkbox-line"><input type="checkbox" name="auto_update" value="1" <?=!empty($source['auto_update'])?'checked':''?>> Automatisch aktualisieren</label>
                    </div>
                    <button class="btn btn-primary">Quelle speichern & prüfen</button>
                  </form>
                  <?php if($source):?>
                  <div class="toolbar top-gap">
                    <form method="post" action="/plugins/<?=rawurlencode($pid)?>/check">
                      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
                      <button class="btn btn-secondary btn-small">GitHub jetzt prüfen</button>
                    </form>
                    <form method="post" action="/plugins/<?=rawurlencode($pid)?>/source/delete" data-confirm="Updatequelle wirklich entfernen?">
                      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
                      <button class="btn btn-danger btn-small">Updatequelle entfernen</button>
                    </form>
                  </div>
                  <?php endif;?>
                </div>
              </details>
              <?php endif;?>
            </div>
          </details>
        <?php endforeach;?>
        </div>
      <?php endif;?>
    </div>
  </details>
<?php endforeach;?>
</div>

<?php if(Auth::can('plugins.update')||Auth::can('plugins.rollback')||Auth::can('plugins.repository.manage')):?>
<details class="panel plugin-runs-accordion">
  <summary class="plugin-server-summary">
    <span class="plugin-server-summary-main">
      <strong>Letzte Plugin-Updatevorgänge</strong>
      <small>Historie nur bei Bedarf aufklappen</small>
    </span>
    <span class="plugin-disclosure">Aufklappen</span>
  </summary>
  <div class="plugin-server-body">
    <div class="table-wrap">
      <table>
        <thead><tr><th>Zeit</th><th>Server</th><th>Plugin</th><th>Version</th><th>Modus</th><th>Status</th></tr></thead>
        <tbody>
        <?php foreach($recentRuns as $run):?>
          <tr>
            <td><?=Security::e((string)$run['created_at'])?></td>
            <td><?=Security::e((string)$run['server_name'])?></td>
            <td class="mono"><?=Security::e((string)$run['plugin_id'])?></td>
            <td><?=Security::e((string)($run['from_version']??'—'))?> → <?=Security::e((string)($run['to_version']??'—'))?></td>
            <td><?=Security::e((string)$run['mode'])?></td>
            <td><?=Security::e((string)$run['status'])?><?php if(!empty($run['message'])):?><div class="small-muted"><?=Security::e((string)$run['message'])?></div><?php endif;?></td>
          </tr>
        <?php endforeach;?>
        <?php if(empty($recentRuns)):?><tr><td colspan="6" class="muted">Noch keine Plugin-Updates ausgeführt.</td></tr><?php endif;?>
        </tbody>
      </table>
    </div>
  </div>
</details>
<?php endif;?>
