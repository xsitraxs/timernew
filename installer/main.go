//go:build windows

// TimerSetup — однофайловый установщик приложения «Таймер».
//
// Внутрь EXE встроен payload.zip (генерируется в GitHub Actions и НЕ хранится
// в репозитории): пакет TimerApp.msix, среда выполнения Windows App Runtime,
// сертификат и install.ps1. При запуске установщик:
//  1. запрашивает права администратора (UAC),
//  2. распаковывает payload во временную папку,
//  3. запускает install.ps1 и ждёт его завершения,
//  4. удаляет временную папку.
//
// Для пользователя это один файл: скачал TimerSetup.exe — запустил — готово.
package main

import (
	"archive/zip"
	"bytes"
	_ "embed"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"unsafe"
)

//go:embed payload.zip
var payloadZip []byte

var (
	advapi32 = syscall.NewLazyDLL("advapi32.dll")
	kernel32 = syscall.NewLazyDLL("kernel32.dll")
	shell32  = syscall.NewLazyDLL("shell32.dll")

	procGetCurrentProcess    = kernel32.NewProc("GetCurrentProcess")
	procOpenProcessToken     = advapi32.NewProc("OpenProcessToken")
	procGetTokenInformation  = advapi32.NewProc("GetTokenInformation")
	procShellExecuteW        = shell32.NewProc("ShellExecuteW")
)

func main() {
	fmt.Println("=== Установка приложения «Таймер» ===")
	fmt.Println()

	if err := run(); err != nil {
		fmt.Println()
		fmt.Println("ОШИБКА УСТАНОВКИ:", err)
		fmt.Println()
		fmt.Println("Что можно попробовать:")
		fmt.Println("  1. Запустите TimerSetup.exe ещё раз и разрешите права администратора.")
		fmt.Println("  2. Включите режим разработчика: Параметры -> Для разработчиков.")
		fmt.Println("  3. Если Windows показывает синее предупреждение SmartScreen:")
		fmt.Println("     «Подробнее» -> «Выполнить в любом случае».")
		waitEnter()
		os.Exit(1)
	}

	fmt.Println()
	fmt.Println("Готово! Откройте меню «Пуск» и введите: Таймер")
	waitEnter()
}

func run() error {
	// Без прав администратора нельзя поставить сертификат — перезапускаем
	// сами себя через UAC и выходим.
	if !isAdmin() {
		fmt.Println("Запрашиваю права администратора...")
		return relaunchAsAdmin()
	}

	dir, err := os.MkdirTemp("", "TimerSetup-*")
	if err != nil {
		return fmt.Errorf("не удалось создать временную папку: %w", err)
	}
	defer os.RemoveAll(dir)

	fmt.Println("Распаковываю файлы установщика...")
	if err := extractPayload(dir); err != nil {
		return err
	}

	script := filepath.Join(dir, "install.ps1")
	if _, err := os.Stat(script); err != nil {
		return fmt.Errorf("в установщике не найден install.ps1: %w", err)
	}

	fmt.Println("Устанавливаю приложение (это может занять до минуты)...")
	fmt.Println()

	cmd := exec.Command("powershell.exe",
		"-NoProfile", "-ExecutionPolicy", "Bypass",
		"-File", script, "-NoPause")
	cmd.Dir = dir
	cmd.Stdin = os.Stdin
	cmd.Stdout = os.Stdout
	cmd.Stderr = os.Stderr

	if err := cmd.Run(); err != nil {
		return fmt.Errorf("установка завершилась с ошибкой: %w", err)
	}

	return nil
}

func extractPayload(dir string) error {
	reader, err := zip.NewReader(bytes.NewReader(payloadZip), int64(len(payloadZip)))
	if err != nil {
		return fmt.Errorf("повреждён встроенный архив установщика: %w", err)
	}

	cleanDir := filepath.Clean(dir)

	for _, file := range reader.File {
		target := filepath.Join(cleanDir, file.Name)

		// Защита от выхода за пределы временной папки (zip slip).
		if target != cleanDir &&
			!strings.HasPrefix(target, cleanDir+string(os.PathSeparator)) {
			return fmt.Errorf("небезопасный путь в архиве: %s", file.Name)
		}

		if file.FileInfo().IsDir() {
			if err := os.MkdirAll(target, 0o755); err != nil {
				return err
			}
			continue
		}

		if err := os.MkdirAll(filepath.Dir(target), 0o755); err != nil {
			return err
		}

		source, err := file.Open()
		if err != nil {
			return err
		}

		destination, err := os.Create(target)
		if err != nil {
			source.Close()
			return err
		}

		_, copyErr := io.Copy(destination, source)
		closeErr := destination.Close()
		source.Close()

		if copyErr != nil {
			return copyErr
		}
		if closeErr != nil {
			return closeErr
		}
	}

	return nil
}

func isAdmin() bool {
	const (
		tokenQuery     = 0x0008
		tokenElevation = 20
	)

	currentProcess, _, _ := procGetCurrentProcess.Call()

	var token syscall.Handle
	result, _, _ := procOpenProcessToken.Call(
		currentProcess,
		uintptr(tokenQuery),
		uintptr(unsafe.Pointer(&token)))
	if result == 0 {
		return false
	}
	defer syscall.CloseHandle(token)

	var elevation uint32
	var returned uint32
	result, _, _ = procGetTokenInformation.Call(
		uintptr(token),
		uintptr(tokenElevation),
		uintptr(unsafe.Pointer(&elevation)),
		uintptr(unsafe.Sizeof(elevation)),
		uintptr(unsafe.Pointer(&returned)))

	return result != 0 && elevation != 0
}

func relaunchAsAdmin() error {
	executable, err := os.Executable()
	if err != nil {
		return err
	}

	verb, err := syscall.UTF16PtrFromString("runas")
	if err != nil {
		return err
	}

	file, err := syscall.UTF16PtrFromString(executable)
	if err != nil {
		return err
	}

	workingDir, err := os.Getwd()
	if err != nil {
		return err
	}

	directory, err := syscall.UTF16PtrFromString(workingDir)
	if err != nil {
		return err
	}

	const swShownNormal = 1

	result, _, callErr := procShellExecuteW.Call(
		0,
		uintptr(unsafe.Pointer(verb)),
		uintptr(unsafe.Pointer(file)),
		0,
		uintptr(unsafe.Pointer(directory)),
		uintptr(swShownNormal))

	// ShellExecute возвращает значение > 32 при успехе.
	if result <= 32 {
		return fmt.Errorf("не удалось запросить права администратора (код %d): %v", result, callErr)
	}

	return nil
}

func waitEnter() {
	fmt.Println()
	fmt.Print("Нажмите Enter, чтобы закрыть окно...")
	var input string
	fmt.Scanln(&input)
}
