function modbuscrc16(str)
	local flag=0xFFFF
	local temp=0xFFFF
	for i=1, string.len(str) do
	    temp=temp~string.byte(str,i)
	    for j=1, 8 do
	    	flag=temp&0x0001
	        temp=temp>>1
	        if (flag>0)
	        --if LSB is set, XOR is 0xA001
	        then temp =temp ~ 0xA001
	        end
	    end
	    
	end
	--高低字节顺序对换
	temp = ((temp << 8)&0xFF00) | (temp >> 8);
	--返回4字节hex string
	return string.format("%04X",temp)
end

return uartData..modbuscrc16(uartData):fromHex()