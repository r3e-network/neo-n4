#!/bin/bash

echo "========================================"
echo "   Neo N4 技术演示视频启动器"
echo "========================================"
echo ""

# 检查 Node.js 是否安装
if ! command -v node &> /dev/null; then
    echo "[错误] 未检测到 Node.js！"
    echo "请先安装 Node.js: https://nodejs.org/"
    exit 1
fi

echo "[1/3] 检查依赖..."
if [ ! -d "node_modules" ]; then
    echo "[2/3] 首次运行，正在安装依赖..."
    npm install
    if [ $? -ne 0 ]; then
        echo "[错误] 依赖安装失败！"
        exit 1
    fi
else
    echo "[2/3] 依赖已安装"
fi

echo "[3/3] 启动开发服务器..."
echo ""
echo "========================================"
echo "  浏览器将自动打开演示页面"
echo "  URL: http://localhost:3000"
echo "  按 Ctrl+C 停止服务器"
echo "========================================"
echo ""

npm run dev
