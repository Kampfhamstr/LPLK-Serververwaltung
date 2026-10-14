<?php
namespace App\Services;

interface ServerConnectorInterface
{
    public function list(string $path): array;
    public function read(string $path): string;
    public function write(string $path, string $content): void;
    public function exists(string $path): bool;
    public function label(): string;
}
