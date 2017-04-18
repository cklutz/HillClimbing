args <- commandArgs(TRUE)

data <- read.csv(args[1])

library(ggplot2)

ggplot(data, aes(x = Time)) + 
  geom_point(aes(y = Throughput, colour="Throughput"), size=1, shape=4) + 
  geom_line(aes(y = Threads, colour="Threads")) +
  labs(title = "", x = "Time (sec)", y = "Throughput/#Threads", color = "") +
  scale_colour_manual(values=c("red", "blue"))

ggsave(args[2], width=280, height=140, units="mm")
