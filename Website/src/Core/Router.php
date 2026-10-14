<?php
namespace App\Core;

final class Router
{
    private array $routes = [];

    public function get(string $path, callable|array $handler): void { $this->add('GET', $path, $handler); }
    public function post(string $path, callable|array $handler): void { $this->add('POST', $path, $handler); }

    private function add(string $method, string $path, callable|array $handler): void
    {
        $paramNames = [];
        $regex = preg_replace_callback('/\{([a-zA-Z_][a-zA-Z0-9_]*)\}/', function ($m) use (&$paramNames) {
            $paramNames[] = $m[1];
            return '([^/]+)';
        }, $path);
        $this->routes[] = [$method, '#^' . $regex . '$#', $paramNames, $handler];
    }

    public function dispatch(string $method, string $uri): void
    {
        if (strtoupper($method) === 'HEAD') $method = 'GET';
        $path = parse_url($uri, PHP_URL_PATH) ?: '/';
        foreach ($this->routes as [$m, $regex, $names, $handler]) {
            if ($m !== $method) continue;
            if (!preg_match($regex, $path, $matches)) continue;
            array_shift($matches);
            $params = [];
            foreach ($names as $i => $name) $params[$name] = urldecode($matches[$i]);
            if (is_array($handler)) {
                [$class, $methodName] = $handler;
                (new $class())->{$methodName}(...array_values($params));
            } else {
                $handler(...array_values($params));
            }
            return;
        }
        http_response_code(404);
        View::render('error', ['title' => 'Nicht gefunden', 'message' => 'Die angeforderte Seite wurde nicht gefunden.']);
    }
}
