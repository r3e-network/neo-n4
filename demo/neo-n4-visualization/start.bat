@echo off
echo ========================================
echo    Neo N4 技术演示视频启动器
echo ========================================
echo.

REM 检查 Node.js 是否安装
where node >nul 2>nul
if %errorlevel% neq 0 (
    echo [错误] 未检测到 Node.js！
    echo 请先安装 Node.js: https://nodejs.org/
    pause
    exit /b 1
)

echo [1/3] 检查依赖...
if not exist node_modules (
    echo [2/3] 首次运行，正在安装依赖...
    call npm install
    if %errorlevel% neq 0 (
        echo [错误] 依赖安装失败！
        pause
        exit /b 1
    )
) else (
    echo [2/3] 依赖已安装
)

echo [3/3] 启动开发服务器...
echo.
echo ========================================
echo   浏览器将自动打开演示页面
echo   URL: http://localhost:3000
echo   按 Ctrl+C 停止服务器
echo ========================================
echo.

call npm run dev

pause
