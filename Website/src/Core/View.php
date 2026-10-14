<?php
namespace App\Core;

final class View
{
    public static function render(string $template, array $viewData = []): void
    {
        if ($template === 'error' && ($_SERVER['REQUEST_METHOD'] ?? '') === 'POST') {
            \App\Services\PanelFormDraft::remember((string)parse_url($_SERVER['REQUEST_URI']??'',PHP_URL_PATH),$_POST,(int)(Auth::user()['id']??0));
        }
        // $viewData statt $data: Views dürfen selbst bewusst eine Variable
        // namens $data erhalten. Mit EXTR_SKIP kollidierte der frühere
        // Parametername mit genau diesem Schlüssel und konnte dadurch
        // Template-Daten unbemerkt verschlucken.
        extract($viewData, EXTR_SKIP);
        $templateFile = dirname(__DIR__, 2) . '/templates/' . $template . '.php';
        if (!is_file($templateFile)) throw new \RuntimeException('Template nicht gefunden: ' . $template);
        ob_start();
        require $templateFile;
        $content = ob_get_clean();
        require dirname(__DIR__, 2) . '/templates/layout.php';
    }

    public static function flash(string $type, string $message): void
    {
        if ($type === 'error' && ($_SERVER['REQUEST_METHOD'] ?? '') === 'POST') {
            \App\Services\PanelFormDraft::remember((string)parse_url($_SERVER['REQUEST_URI']??'',PHP_URL_PATH),$_POST,(int)(Auth::user()['id']??0));
        }
        $_SESSION['_flash'][] = ['type' => $type, 'message' => $message];
    }

    public static function consumeFlashes(): array
    {
        $items = $_SESSION['_flash'] ?? [];
        unset($_SESSION['_flash']);
        return $items;
    }
}
