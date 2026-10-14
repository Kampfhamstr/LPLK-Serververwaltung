<?php
namespace App\Controllers;
use App\Core\Auth; use App\Core\Database; use App\Core\View;
final class AuditController { public function index():void { Auth::requirePermission('audit.view');$rows=Database::connection()->query('SELECT a.*,u.name user_name FROM audit_log a LEFT JOIN users u ON u.id=a.user_id ORDER BY a.id DESC LIMIT 500')->fetchAll();View::render('audit',['title'=>'Audit-Log','rows'=>$rows]); }}
